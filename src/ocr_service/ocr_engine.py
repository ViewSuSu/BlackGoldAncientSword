"""
PaddleOCR 引擎模块 —— 永劫无间战绩助手

通过 get_ocr() 提供 PaddleOCR 全局单例，供 C# 端通过 Python.NET 调用。
"""
import os
from paddleocr import PaddleOCR

# 全局单例，延迟初始化
_ocr_instance = None


def get_ocr(lang: str = "ch", use_gpu: bool = False) -> PaddleOCR:
    """
    获取全局共享的 PaddleOCR 实例（单例、延迟初始化）。

    参数:
        lang: 识别语言，"ch" 为中英文混合，"en" 为英文，"japan" 为日文等。
        use_gpu: 是否使用 GPU 加速。默认 False（CPU 推理）。

    返回:
        PaddleOCR 实例。
    """
    global _ocr_instance
    if _ocr_instance is None:
        _ocr_instance = PaddleOCR(
            lang=lang,
            use_gpu=use_gpu,
            use_angle_cls=True,   # 启用文字方向分类，提高竖排/倒置文字识别率
            show_log=False,       # 关闭调试日志，避免污染 C# 端输出
        )
    return _ocr_instance


def recognize(image_path: str):
    """
    对指定图片执行 OCR 识别。

    参数:
        image_path: 图片文件的完整路径。

    返回:
        列表的列表：每页是一个 [(bbox, (text, confidence)), ...] 的列表。
        bbox 为 [[x1,y1],[x2,y2],[x3,y3],[x4,y4]] 格式的四边形。
    """
    ocr = get_ocr()
    return ocr.ocr(image_path)


def recognize_text(image_path: str) -> str:
    """
    执行 OCR 并返回拼接后的纯文本（按行用换行符分隔）。

    参数:
        image_path: 图片文件的完整路径。

    返回:
        识别的纯文本字符串。未检测到文字时返回空字符串。
    """
    results = recognize(image_path)
    if not results or not results[0]:
        return ""
    return "\n".join(line[1][0] for line in results[0])


def reset():
    """
    释放当前 OCR 实例。下次调用 get_ocr() 会重新创建。
    通常在切换识别语言或需要释放显存时调用。
    """
    global _ocr_instance
    _ocr_instance = None
