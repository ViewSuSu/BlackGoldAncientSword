using Mapster;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Extensions
{
    public static class ConverterExtensions
    {
        public static TDestination ConvertTo<TSource, TDestination>(this TSource source)
            => source.Adapt<TDestination>();

        public static List<TDestination> ConvertToList<TDestination>(this IEnumerable<object> sourceList)
            => sourceList.Adapt<List<TDestination>>();
    }
}
