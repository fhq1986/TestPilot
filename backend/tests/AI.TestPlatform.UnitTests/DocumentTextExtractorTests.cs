using System.IO.Compression;
using System.Text;
using AI.TestPlatform.Api.Modules.AI;

namespace AI.TestPlatform.UnitTests;

public class DocumentTextExtractorTests
{
    private static MemoryStream TextStream(string content, Encoding encoding)
        => new(encoding.GetBytes(content));

    /// <summary>构造最小可用的 .docx（zip：word/document.xml）。</summary>
    private static MemoryStream DocxStream(params string[] paragraphs)
    {
        var body = string.Join("", paragraphs.Select(p =>
            $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"));
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>{body}</w:body>
            </w:document>
            """;

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void 提取UTF8文本文件()
    {
        using var stream = TextStream("用户登录功能\n支持用户名密码登录", new UTF8Encoding(false));
        var doc = DocumentTextExtractor.Extract(stream, "需求.txt");

        Assert.Equal("用户登录功能\n支持用户名密码登录", doc.Text);
        Assert.Equal(2, doc.Text.Split('\n').Length);
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public void 提取GB18030文本文件()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb = Encoding.GetEncoding("GB18030");
        using var stream = TextStream("登录功能需求：连续3次失败锁定1分钟", gb);

        var doc = DocumentTextExtractor.Extract(stream, "需求.txt");

        Assert.Contains("连续3次失败锁定1分钟", doc.Text);
        Assert.Contains(doc.Warnings, w => w.Contains("GB18030"));
    }

    [Fact]
    public void 提取docx段落并保留换行()
    {
        using var stream = DocxStream("1. 用户登录", "2. 密码错误提示", "3. 连续三次锁定 1 分钟");

        var doc = DocumentTextExtractor.Extract(stream, "需求说明书.docx");

        Assert.Contains("1. 用户登录", doc.Text);
        Assert.Contains("3. 连续三次锁定 1 分钟", doc.Text);
        Assert.Equal(3, doc.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void 超过上限时截断并给出提示()
    {
        var longText = new string('需', DocumentTextExtractor.MaxChars + 500);
        using var stream = TextStream(longText, new UTF8Encoding(false));

        var doc = DocumentTextExtractor.Extract(stream, "长文档.txt");

        Assert.Equal(DocumentTextExtractor.MaxChars, doc.Text.Length);
        Assert.Contains(doc.Warnings, w => w.Contains("已截取"));
    }

    [Fact]
    public void 不支持的类型给出可读错误()
    {
        using var stream = TextStream("x", new UTF8Encoding(false));
        var ex = Assert.Throws<InvalidOperationException>(
            () => DocumentTextExtractor.Extract(stream, "需求.xlsx"));
        Assert.Contains("不支持的文件类型", ex.Message);
    }

    [Fact]
    public void PDF给出转换建议()
    {
        using var stream = TextStream("%PDF-1.4", new UTF8Encoding(false));
        var ex = Assert.Throws<InvalidOperationException>(
            () => DocumentTextExtractor.Extract(stream, "需求.pdf"));
        Assert.Contains("暂不支持 PDF", ex.Message);
    }

    [Fact]
    public void 空文档报错()
    {
        using var stream = TextStream("   \n\n  ", new UTF8Encoding(false));
        Assert.Throws<InvalidOperationException>(
            () => DocumentTextExtractor.Extract(stream, "空.txt"));
    }

    [Fact]
    public void 连续空行被压缩()
    {
        using var stream = TextStream("第一段\n\n\n\n第二段", new UTF8Encoding(false));
        var doc = DocumentTextExtractor.Extract(stream, "需求.md");
        Assert.Equal("第一段\n\n第二段", doc.Text);
    }
}
