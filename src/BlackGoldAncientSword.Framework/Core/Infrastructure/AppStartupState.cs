namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{

    public static class AppStartupState
    {

        private static volatile bool _pipelineReady;

        public static bool IsPipelineReady => _pipelineReady;

        public static void MarkPipelineReady() => _pipelineReady = true;

        private static volatile bool _loginRestored;

        /// <summary>
        /// 启动流程里的登录态恢复是否已走完（成功或失败都算"走完"）。
        /// <para>
        /// 与 <see cref="IsPipelineReady"/> 的区别：管线就绪只表示请求能发出去（签名 / 公共参数已装），
        /// 而登录态（cookie）是**之后**才异步恢复的——在那之前发出的请求会被服务端按未登录拒绝。
        /// 启动后很快进入的页面（战绩页等）要等这个标志再发首轮请求，否则第一次加载必然失败。
        /// </para>
        /// </summary>
        public static bool IsLoginRestored => _loginRestored;

        public static void MarkLoginRestored() => _loginRestored = true;
    }
}
