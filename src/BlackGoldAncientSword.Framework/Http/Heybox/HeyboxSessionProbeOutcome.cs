using BlackGoldAncientSword.Framework.Http.Generated;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public readonly record struct HeyboxSessionProbeOutcome(bool Alive, HeyboxHomeResponse? Home);
}
