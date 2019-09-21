﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Substrate.MediaWiki.Remote;

namespace Substrate.Edge.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PagesController : ControllerBase
    {
        private MediaWikiApiServices _apiService;
        private static readonly TimeSpan TokenValidity = TimeSpan.FromDays(2);

        public PagesController(MediaWikiApiServices apiService)
        {
            _apiService = apiService;
        }

        [HttpGet("{title}")]
        public async Task<IActionResult> Get(string title)
        {
            if (_apiService.CurrentIdentity == null ||
                DateTimeOffset.Now - _apiService.LastLogin > TokenValidity)
            {
                await _apiService.LoginAsync();
            }

            var (metadata, content) = await _apiService.GetPageAsync(title, null);
            if (!string.IsNullOrEmpty(content))
            {
                return Content(content, "text/html; charset=utf-8");
            }

            return NotFound();
        }
    }
}