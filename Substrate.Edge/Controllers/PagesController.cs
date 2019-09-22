using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Substrate.Edge.Caching;
using Substrate.MediaWiki.Remote;

namespace Substrate.Edge.Controllers
{
    [Controller]
    public class PagesController : ControllerBase
    {
        private PageRepository _cache;
        private MediaWikiApiServices _apiService;

        private const string ContentType = "text/html; charset=utf-8";

        public PagesController(MediaWikiApiServices apiService, PageRepository cache)
        {
            _apiService = apiService;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetPage()
        {
            if (Request.Path.Value != null)
            {
                // Trim the forward slash
                var title = Request.Path.Value.Substring(1);

                // Cache
                var cacheContent = _cache.GetPageContent(title);
                if (cacheContent != null)
                {
                    return File(cacheContent, ContentType);
                }

                var (metadata, newContent) = await _apiService.GetPageAsync(title, null);
                if (newContent != null)
                {
                    _cache.PutPageContent(title, metadata, newContent);
                    return File(newContent, ContentType);
                }
            }
            
            return NotFound();
        }
    }
}
