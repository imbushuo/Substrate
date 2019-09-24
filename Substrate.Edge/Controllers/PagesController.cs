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
        public IActionResult Index()
        {
            return Redirect("/Mainpage");
        }

        [HttpGet]
        public async Task<IActionResult> GetPage(string id)
        {
            if (id != null)
            {
                // Cache
                var cacheContent = _cache.GetPageContent(id);
                if (cacheContent != null)
                {
                    return File(cacheContent, ContentType);
                }

                var (metadata, newContent) = await _apiService.GetPageAsync(id, null);
                if (newContent != null)
                {
                    _cache.PutPageContent(id, metadata, newContent);
                    return File(newContent, ContentType);
                }
            }
            
            return NotFound();
        }

        [HttpGet]
        public IActionResult GetMetadata(string id)
        {
            if (id != null)
            {
                var metadata = _cache.GetPageMetadata(id);
                if (metadata != null)
                {
                    return Ok(metadata);
                }
            }

            return NotFound();
        }
    }
}
