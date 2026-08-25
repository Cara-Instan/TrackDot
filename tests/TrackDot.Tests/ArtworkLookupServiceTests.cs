using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

public class ArtworkLookupServiceTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task GetArtworkUrlAsync_WhenTitleIsEmpty_ReturnsNull()
    {
        var service = new ArtworkLookupService();
        var result = await service.GetArtworkUrlAsync(string.Empty, "Artist");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetArtworkUrlAsync_WhenItunesSucceeds_ReturnsUpgradedHighResUrl()
    {
        var jsonResponse = """
        {
            "resultCount": 1,
            "results": [
                {
                    "trackName": "Love Drought",
                    "artistName": "Beyoncé",
                    "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music116/v4/cover.jpg/100x100bb.jpg"
                }
            ]
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host.Contains("apple.com"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jsonResponse)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var service = new ArtworkLookupService(client);

        var result = await service.GetArtworkUrlAsync("Love Drought", "Beyoncé");

        Assert.NotNull(result);
        Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/Music116/v4/cover.jpg/600x600bb.jpg", result);
    }

    [Fact]
    public async Task GetArtworkUrlAsync_WhenItunesEmpty_FallsBackToDeezer()
    {
        var itunesEmpty = """{ "resultCount": 0, "results": [] }""";
        var deezerResponse = """
        {
            "total": 1,
            "data": [
                {
                    "title": "Love Drought",
                    "album": {
                        "cover_xl": "https://cdn-images.dzcdn.net/images/cover/lemonade/1000x1000.jpg"
                    }
                }
            ]
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host.Contains("apple.com"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(itunesEmpty)
                };
            }
            if (req.RequestUri.Host.Contains("deezer.com"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(deezerResponse)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var service = new ArtworkLookupService(client);

        var result = await service.GetArtworkUrlAsync("Love Drought", "Beyoncé");

        Assert.NotNull(result);
        Assert.Equal("https://cdn-images.dzcdn.net/images/cover/lemonade/1000x1000.jpg", result);
    }

    [Fact]
    public async Task GetArtworkUrlAsync_CachesResult()
    {
        int requestCount = 0;
        var jsonResponse = """
        {
            "resultCount": 1,
            "results": [
                {
                    "trackName": "Song",
                    "artistName": "Artist",
                    "artworkUrl100": "https://is1-ssl.mzstatic.com/cover/100x100bb.jpg"
                }
            ]
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
        });

        var client = new HttpClient(handler);
        var service = new ArtworkLookupService(client);

        var result1 = await service.GetArtworkUrlAsync("Song", "Artist");
        var result2 = await service.GetArtworkUrlAsync("Song", "Artist");

        Assert.Equal(result1, result2);
        Assert.Equal(1, requestCount);
    }
}

