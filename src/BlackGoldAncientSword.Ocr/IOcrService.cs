namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// OCR 服务接口。提供图片文字识别的核心能力。
/// </summary>
public interface IOcrService
{
    /// <summary>对指定图片执行 OCR 识别。</summary>
    [Obsolete("使用 RecognizeAsync 替代；同步桥接保留仅为接口兼容，存在 ThreadPool 饥饿风险")]
    List<OcrResult> Recognize(string imagePath);

    /// <summary>异步对指定图片执行 OCR 识别。</summary>
    Task<List<OcrResult>> RecognizeAsync(string imagePath);

    /// <summary>异步对内存中的图片字节数据执行 OCR 识别。</summary>
    Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes);

    /// <summary>对指定图片执行 OCR，返回拼接后的纯文本。</summary>
    string RecognizeText(string imagePath);

    /// <summary>异步对指定图片执行 OCR，返回拼接后的纯文本。</summary>
    Task<string> RecognizeTextAsync(string imagePath);

    /// <summary>异步对内存中的图片字节数据执行 OCR，返回拼接后的纯文本。</summary>
    Task<string> RecognizeTextAsync(byte[] imageBytes);
}
