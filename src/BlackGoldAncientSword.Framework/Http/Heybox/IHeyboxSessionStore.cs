namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public interface IHeyboxSessionStore
    {
        HeyboxSession? Load();

        void Save(HeyboxSession session);

        void Clear();
    }
}
