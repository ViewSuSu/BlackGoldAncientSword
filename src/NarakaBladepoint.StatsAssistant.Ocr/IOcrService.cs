namespace NarakaBladepoint.StatsAssistant.Ocr;

/// <summary>
/// OCR 服务接口。提供图片文字识别的核心能力。
/// </summary>
public interface IOcrService
{
    /// <summary>对指定图片执行 OCR 识别。</summary>
    List<OcrResult> Recognize(string imagePath);

    /// <summary>异步对指定图片执行 OCR 识别。</summary>
    Task<List<OcrResult>> RecognizeAsync(string imagePath);

    /// <summary>对内存中的图片字节数据执行 OCR 识别。</summary>
    List<OcrResult> Recognize(byte[] imageBytes);

    /// <summary>异步对内存中的图片字节数据执行 OCR 识别。</summary>
    Task<List<OcrResult>> RecognizeAsync(byte[] imageBytes);

    /// <summary>对指定图片执行 OCR，返回拼接后的纯文本。</summary>
    string RecognizeText(string imagePath);

    /// <summary>异步对指定图片执行 OCR，返回拼接后的纯文本。</summary>
    Task<string> RecognizeTextAsync(string imagePath);

    /// <summary>对内存中的图片字节数据执行 OCR，返回拼接后的纯文本。</summary>
    string RecognizeText(byte[] imageBytes);

    /// <summary>异步对内存中的图片字节数据执行 OCR，返回拼接后的纯文本。</summary>
    Task<string> RecognizeTextAsync(byte[] imageBytes);
}
