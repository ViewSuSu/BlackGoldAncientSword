namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// OCR 服务接口。提供图片文字识别的核心能力。
/// 仅支持内存字节流入参：调用方负责把图片裁剪/编码成 PNG 字节，
/// 然后由 OcrEngine 通过 image_base64 直接喂给常驻 PaddleOCR-json 子进程，零磁盘 IO。
/// </summary>
public interface IOcrService
{
    /// <summary>异步对内存中的图片字节数据执行 OCR 识别。</summary>
    Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes);

    /// <summary>异步对内存中的图片字节数据执行 OCR，返回拼接后的纯文本。</summary>
    Task<string> RecognizeTextAsync(byte[] imageBytes);
}
