using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace AI.TestPlatform.Api.Modules.AI;

public record ExtractedDocument(string FileName, string Text, List<string> Warnings)
{
    public int CharCount => Text.Length;
}

/// <summary>
/// 需求文档文本提取：支持 .txt / .md / .docx（Word），供「AI 生成用例」按文档内容生成用例。
/// .docx 直接解压读取 word/document.xml，无需额外依赖。
/// </summary>
public static class DocumentTextExtractor
{
    /// <summary>单次提取的最大字符数（超出部分截断，避免超出模型上下文）。</summary>
    public const int MaxChars = 20000;

    public static readonly string[] SupportedExtensions = { ".txt", ".md", ".markdown", ".docx" };

    public static ExtractedDocument Extract(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var warnings = new List<string>();

        var raw = extension switch
        {
            ".txt" or ".md" or ".markdown" => ReadPlainText(stream, warnings),
            ".docx" => ReadDocx(stream),
            ".pdf" => throw new InvalidOperationException(
                "暂不支持 PDF：请将需求文档另存为 Word（.docx）或文本（.txt/.md）后再上传"),
            _ => throw new InvalidOperationException(
                $"不支持的文件类型 {extension}，当前支持：{string.Join("、", SupportedExtensions)}"),
        };

        var text = Normalize(raw);
        if (text.Length == 0)
            throw new InvalidOperationException("文档内容为空，未能提取到任何文字（扫描版/图片型文档无法解析）");

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars];
            warnings.Add($"文档较长，已截取前 {MaxChars} 字用于生成用例");
        }

        return new ExtractedDocument(fileName, text, warnings);
    }

    /// <summary>纯文本读取：优先按 BOM/UTF-8 解析，失败时回退 GB18030（中文需求文档常见编码）。</summary>
    private static string ReadPlainText(Stream stream, List<string> warnings)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return utf8.GetString(RemoveBom(bytes));
        }
        catch (DecoderFallbackException)
        {
            // 非 UTF-8：按 GB18030 解析（覆盖 GBK/GB2312）
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gb = Encoding.GetEncoding("GB18030");
            warnings.Add("文件不是 UTF-8 编码，已按 GB18030 解析");
            return gb.GetString(RemoveBom(bytes));
        }
        catch (ArgumentException)
        {
            warnings.Add("文件编码无法识别，已按 UTF-8 宽松解析（可能出现乱码）");
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>docx 解析：读取 word/document.xml，段落转行、制表符保留。</summary>
    private static string ReadDocx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("不是有效的 .docx 文件（缺少 word/document.xml）");

        using var entryStream = entry.Open();
        var doc = XDocument.Load(entryStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var builder = new StringBuilder();
        foreach (var paragraph in doc.Descendants(w + "p"))
        {
            var text = string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value));
            // 制表符与换行在 Word 中用独立元素表示
            if (paragraph.Descendants(w + "tab").Any())
                text = text.Replace("\t", "    ");
            builder.AppendLine(text);
        }

        // 表格内容：Word 中表格段落同样在 w:p 内，已包含在上面的遍历中
        return builder.ToString();
    }

    private static byte[] RemoveBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return bytes[3..];
        return bytes;
    }

    /// <summary>压缩空白：连续空行合并、去首尾空白，让提示词更紧凑。</summary>
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        var result = new List<string>();
        var blankRun = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blankRun++;
                if (blankRun > 1)
                    continue;
            }
            else
            {
                blankRun = 0;
            }
            result.Add(line);
        }

        return string.Join("\n", result).Trim();
    }
}
