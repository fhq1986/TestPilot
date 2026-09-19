using AI.TestPlatform.Application.AI;
using Microsoft.Playwright;
using System.Text.Json;

namespace AI.TestPlatform.Api.AI;

public static class DomExtractor
{
    public const string Script = """
        () => {
          // li 必须收集：侧边栏菜单（el-menu-item）与下拉选项（el-select-dropdown__item）
          // 都渲染成 li，不收的话 AI 永远看不到它们（只能匹配到主内容区的 a/button）。
          // el-select__wrapper/trigger 必须收集：下拉的占位文本（如「默认当前用户」）渲染在
          // wrapper 内部的 span 里，不收的话 AI 看到的下拉只是一个没有文本的空 input。
          const tags = ['button', 'a', 'input', 'select', 'textarea', 'label', 'li',
            '[role="button"]', '[role="menuitem"]', '[role="tab"]', '[role="option"]',
            '.el-select__wrapper', '.el-select__trigger'];
          const els = document.querySelectorAll(tags.join(','));
          const seen = new Map();
          const result = [];
          for (const el of els) {
            if (result.length >= 300) break;
            const rect = el.getBoundingClientRect();
            if (rect.width === 0 && rect.height === 0) continue;
            const tag = el.tagName.toLowerCase();
            const text = ((el.innerText || el.value || '') + '').trim().slice(0, 60);
            const key = tag + '|' + text;
            seen.set(key, (seen.get(key) || 0) + 1);
            const placeholder = (el.getAttribute?.('placeholder')
              || el.querySelector?.('.el-select__placeholder')?.textContent
              || '').trim().slice(0, 60) || null;
            result.push({
              index: result.length,
              tag,
              id: el.id || null,
              class: (typeof el.className === 'string' ? el.className : '').slice(0, 80) || null,
              text,
              aria: el.getAttribute('aria-label') || null,
              placeholder,
            });
          }
          return JSON.stringify(result);
        }
        """;

    public static async Task<List<InteractiveElement>> ExtractAsync(IPage page)
    {
        var raw = await page.EvaluateAsync<string>(Script);
        var list = JsonSerializer.Deserialize<List<InteractiveElement>>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return list ?? new List<InteractiveElement>();
    }

    public static string NormalizePageUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // 必须保留非默认端口：否则 localhost:3000 与 localhost:5173 会共用同一份元素缓存，
            // 导致不同站点互相命中错误的选择器。
            var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            return $"{uri.Scheme}://{authority}{uri.AbsolutePath}".ToLowerInvariant();
        }
        return url;
    }
}
