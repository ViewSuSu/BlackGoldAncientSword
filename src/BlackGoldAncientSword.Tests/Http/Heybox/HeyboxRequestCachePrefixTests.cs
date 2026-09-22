using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{
    public class HeyboxRequestCachePrefixTests
    {
        [Fact]
        public async Task InvalidatePrefix_DropsOnlyMatchingEntries()
        {
            var cache = new HeyboxRequestCache();
            var matched = 0;
            var untouched = 0;

            Task<string> Matched()
            {
                matched++;
                return Task.FromResult("matched");
            }

            Task<string> Untouched()
            {
                untouched++;
                return Task.FromResult("untouched");
            }

            await cache.RunAsync("home|role-a|163||", Matched, CancellationToken.None);
            await cache.RunAsync("home|role-b|163||", Untouched, CancellationToken.None);

            cache.InvalidatePrefix("home|role-a|");

            await cache.RunAsync("home|role-a|163||", Matched, CancellationToken.None);
            await cache.RunAsync("home|role-b|163||", Untouched, CancellationToken.None);

            Assert.Equal(2, matched);
            Assert.Equal(1, untouched);
        }

        [Fact]
        public async Task InvalidatePlayer_DropsEverySeasonAndServerVariant()
        {
            var cache = new HeyboxRequestCache();
            var provider = new HeyboxHomeDataProvider(cache);
            var loads = 0;

            Task<string> Load()
            {
                loads++;
                return Task.FromResult("home");
            }

            await cache.RunAsync("home|role-x|163||", Load, CancellationToken.None);
            await cache.RunAsync("home|role-x|163|pre-01|5000000", Load, CancellationToken.None);
            await cache.RunAsync("home|role-x|164|pre-01|5000001", Load, CancellationToken.None);
            await cache.RunAsync("home|role-y|163||", Load, CancellationToken.None);

            Assert.Equal(4, loads);

            provider.InvalidatePlayer("role-x");

            await cache.RunAsync("home|role-x|163||", Load, CancellationToken.None);
            await cache.RunAsync("home|role-x|163|pre-01|5000000", Load, CancellationToken.None);
            await cache.RunAsync("home|role-x|164|pre-01|5000001", Load, CancellationToken.None);
            await cache.RunAsync("home|role-y|163||", Load, CancellationToken.None);

            Assert.Equal(7, loads);
        }
    }
}
