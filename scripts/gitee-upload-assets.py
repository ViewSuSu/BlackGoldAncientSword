#!/usr/bin/env python
# coding: utf-8
"""
Gitee Release 附件上传脚本

日志风格参考 H-TWINKLE/sync-action：
- logging 时间戳 + 级别
- tqdm 字节级进度条（unit_scale，自动 B/KB/MB）
- MultipartEncoderMonitor 流式上传 + 实时回调进度
- 总进度条 + 每文件单独进度条

保留本仓库特性：
- ThreadPoolExecutor 并行（默认 3 路）
- 单文件 3 次重试 + 指数退避
- 任一文件失败 -> exit 1（触发 workflow rollback）
- 超 100MB 文件自动跳过

环境变量：
- GITEE_REPO         e.g. SususuChang/BlackGoldAncientSword
- GITEE_TOKEN        Gitee personal access token
- GITEE_RELEASE_ID   既有 release 的数字 ID（pwsh 前置 step 创建）
- GITEE_ASSETS_DIR   默认 output
- GITEE_PARALLEL     默认 3
- GITEE_MAX_RETRIES  默认 3
- GITEE_TIMEOUT_CEIL 默认 14400 秒（4 小时）
"""

import os
import sys
import glob
import time
import logging
from concurrent.futures import ThreadPoolExecutor, as_completed

import requests
from requests_toolbelt import MultipartEncoder, MultipartEncoderMonitor
from tqdm import tqdm
from tqdm.contrib.logging import logging_redirect_tqdm

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
)
logger = logging.getLogger('gitee-upload')


GITEE_API_BASE = 'https://gitee.com/api/v5/repos'
MAX_SIZE_BYTES = 100 * 1024 * 1024  # Gitee 单文件上限


def env(key, default=None, required=False):
    v = os.environ.get(key)
    if v is None or v == '':
        if required:
            logger.error('环境变量 %s 未设置', key)
            sys.exit(2)
        return default
    return v


def upload_one(repo, token, release_id, file_path, position, max_retries, timeout_ceiling):
    """上传单文件，带 tqdm 字节进度 + 重试。返回 (name, ok, elapsed, info)。"""
    file_name = os.path.basename(file_path)
    file_size = os.path.getsize(file_path)
    size_mb = file_size / 1024 / 1024
    url = f'{GITEE_API_BASE}/{repo}/releases/{release_id}/attach_files'

    # 公式：size/5120 + 300，下限 600s，上限 timeout_ceiling
    max_time = max(600, min(timeout_ceiling, int(file_size / 5120 + 300)))

    last_err = None
    for attempt in range(1, max_retries + 1):
        if attempt > 1:
            backoff = 15 * (2 ** (attempt - 2))
            logger.warning('[%s] 退避 %ds 后重试 (%d/%d) | 上次失败：%s',
                           file_name, backoff, attempt, max_retries, last_err)
            time.sleep(backoff)

        logger.info('[%s] START attempt=%d/%d size=%.1fMB ceiling=%ds',
                    file_name, attempt, max_retries, size_mb, max_time)

        t0 = time.time()
        try:
            with open(file_path, 'rb') as fh:
                encoder = MultipartEncoder(fields={
                    'access_token': token,
                    'file': (file_name, fh, 'application/octet-stream'),
                })
                with tqdm(
                    total=encoder.len,
                    unit='B',
                    unit_scale=True,
                    unit_divisor=1024,
                    desc=f'上传 {file_name}',
                    position=position,
                    leave=True,
                    mininterval=2.0,
                    miniters=1,
                    ascii=True,
                ) as bar:
                    def cb(monitor):
                        bar.update(monitor.bytes_read - bar.n)

                    monitor = MultipartEncoderMonitor(encoder, cb)
                    resp = requests.post(
                        url,
                        data=monitor,
                        headers={'Content-Type': monitor.content_type},
                        timeout=(30, max_time),
                    )

            elapsed = time.time() - t0
            if 200 <= resp.status_code < 300:
                try:
                    body = resp.json()
                except Exception:
                    body = {}
                dl = body.get('browser_download_url', '<no url>')
                mbps = (size_mb / elapsed) if elapsed > 0 else 0
                logger.info('[%s] DONE  attempt=%d elapsed=%.1fs speed=%.2fMB/s url=%s',
                            file_name, attempt, elapsed, mbps, dl)
                return (file_name, True, elapsed, dl)

            try:
                msg = resp.json().get('message', resp.text[:200])
            except Exception:
                msg = resp.text[:200] if resp.text else ''
            last_err = f'HTTP {resp.status_code}: {msg}'
            logger.error('[%s] FAIL  attempt=%d/%d elapsed=%.1fs %s',
                         file_name, attempt, max_retries, elapsed, last_err)

        except Exception as e:
            elapsed = time.time() - t0
            last_err = repr(e)
            logger.error('[%s] FAIL  attempt=%d/%d elapsed=%.1fs %s',
                         file_name, attempt, max_retries, elapsed, last_err)

    logger.error('[%s] GIVEUP 3 次重试后仍失败：%s', file_name, last_err)
    return (file_name, False, 0.0, last_err)


def main():
    repo = env('GITEE_REPO', required=True)
    token = env('GITEE_TOKEN', required=True)
    release_id = env('GITEE_RELEASE_ID', required=True)
    assets_dir = env('GITEE_ASSETS_DIR', default='output')
    parallel = int(env('GITEE_PARALLEL', default='3'))
    max_retries = int(env('GITEE_MAX_RETRIES', default='3'))
    timeout_ceiling = int(env('GITEE_TIMEOUT_CEIL', default='14400'))

    all_paths = sorted(glob.glob(os.path.join(assets_dir, '*')))
    all_paths = [p for p in all_paths if os.path.isfile(p)]
    if not all_paths:
        logger.error('目录 %s 中未找到任何文件', assets_dir)
        sys.exit(1)

    logger.info('====================================================')
    logger.info('Gitee 上传任务：repo=%s release_id=%s', repo, release_id)
    logger.info('扫描到 %d 个文件 | 并行=%d | 单文件上限=%ds',
                len(all_paths), parallel, timeout_ceiling)
    logger.info('====================================================')

    to_upload = []
    skipped = []
    for p in all_paths:
        size = os.path.getsize(p)
        name = os.path.basename(p)
        size_mb = size / 1024 / 1024
        if size > MAX_SIZE_BYTES:
            logger.warning('[%s] SKIP %.1fMB 超 100MB Gitee 限制', name, size_mb)
            skipped.append((name, size_mb))
        else:
            to_upload.append(p)

    results = []
    if to_upload:
        with logging_redirect_tqdm():
            with tqdm(total=len(to_upload), desc='总进度', unit='file',
                      position=0, leave=True, ascii=True) as master:
                with ThreadPoolExecutor(max_workers=parallel) as ex:
                    futures = {
                        ex.submit(upload_one, repo, token, release_id, fp,
                                  idx + 1, max_retries, timeout_ceiling): fp
                        for idx, fp in enumerate(to_upload)
                    }
                    for fut in as_completed(futures):
                        results.append(fut.result())
                        master.update(1)

    uploaded = [r for r in results if r[1]]
    failed = [r for r in results if not r[1]]

    logger.info('')
    logger.info('===== Per-file recap =====')
    for name, ok, secs, info in sorted(results, key=lambda r: r[0]):
        if ok:
            logger.info('  OK    %-60s %7.1fs   %s', name, secs, info)
        else:
            logger.info('  FAIL  %-60s last=%6.1fs   %s', name, secs, info)
    for name, size_mb in skipped:
        logger.info('  SKIP  %-60s %.1fMB (>100MB)', name, size_mb)
    logger.info('===== uploaded=%d skipped=%d failed=%d =====',
                len(uploaded), len(skipped), len(failed))

    if failed:
        logger.error('有 %d 个文件上传失败，job 标记失败以触发回滚', len(failed))
        sys.exit(1)


if __name__ == '__main__':
    main()
