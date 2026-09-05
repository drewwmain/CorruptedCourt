using UnityEngine;

namespace CorruptedCourt.Core
{
    /// <summary>
    /// Gameplay logging that compiles away completely unless the <c>CC_LOGGING</c> scripting-define
    /// symbol is set.
    ///
    /// <para><see cref="System.Diagnostics.ConditionalAttribute"/> makes the C# compiler drop the
    /// entire call at every call site in any assembly built without the symbol - <b>including the
    /// evaluation of every argument</b>. So <c>Log.Game($"hit {x} at {pos}")</c> costs nothing in a
    /// shipping build: no call, no string interpolation, no boxing, no allocation.</para>
    ///
    /// <para>Only informational / warning chatter goes through here. <c>UnityEngine.Debug.LogError</c>
    /// is deliberately NOT wrapped - real errors must always surface.</para>
    ///
    /// <para>To see these logs in the Editor, add <c>CC_LOGGING</c> to
    /// Project Settings > Player > Other Settings > Scripting Define Symbols.</para>
    /// </summary>
    public static class Log
    {
        [System.Diagnostics.Conditional("CC_LOGGING")]
        public static void Game(object message) => UnityEngine.Debug.Log(message);

        [System.Diagnostics.Conditional("CC_LOGGING")]
        public static void Game(object message, Object context) => UnityEngine.Debug.Log(message, context);

        [System.Diagnostics.Conditional("CC_LOGGING")]
        public static void Warn(object message) => UnityEngine.Debug.LogWarning(message);

        [System.Diagnostics.Conditional("CC_LOGGING")]
        public static void Warn(object message, Object context) => UnityEngine.Debug.LogWarning(message, context);
    }
}
