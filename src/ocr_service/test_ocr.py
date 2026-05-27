"""
PaddleOCR 快速测试脚本。

用法:
    python test_ocr.py <图片路径>

示例:
    python test_ocr.py screenshot.png
"""
import sys
from ocr_engine import recognize, recognize_text

if __name__ == "__main__":
    # 检查命令行参数
    if len(sys.argv) < 2:
        print("用法: python test_ocr.py <图片路径>")
        sys.exit(1)

    image_path = sys.argv[1]
    print(f"正在识别: {image_path}")
    results = recognize(image_path)

    # 无文字检测
    if not results or not results[0]:
        print("未检测到文字。")
        sys.exit(0)

    # 逐行打印每条识别结果（含置信度）
    print(f"\n共检测到 {len(results[0])} 行文字:\n")
    for i, line in enumerate(results[0]):
        bbox, (text, conf) = line
        print(f"  [{i:02d}] 置信度={conf:.3f} | {text}")

    # 打印拼接后的完整文本
    print("\n── 完整文本 ──")
    print(recognize_text(image_path))
