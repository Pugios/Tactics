using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Tactics.Player;

namespace Unity.AI.Assistant.PlayModeTest
{
    [InitializeOnLoad]
    internal static class PlayModeTestRunner
    {
        private const string StateKey = "PlayModeTest.State";
        private const string ResultKey = "PlayModeTest.Result";
        private const string ScriptPathKey = "PlayModeTest.ScriptPath";
        private const string SentinelLog = "PLAY_MODE_TEST_COMPLETE";

        private static readonly int WaitFrames = SessionState.GetInt("PlayModeTest.WaitFrames", 60); // Wait longer for landing

        static PlayModeTestRunner()
        {
            string state = SessionState.GetString(StateKey, "Idle");
            switch (state)
            {
                case "Idle": break;
                case "WaitingForCompile":
                    EditorApplication.delayCall += () =>
                    {
                        SessionState.SetString(StateKey, "EnteringPlayMode");
                        EditorApplication.isPlaying = true;
                    };
                    break;
                case "EnteringPlayMode":
                    if (EditorApplication.isPlaying)
                    {
                        SessionState.SetString(StateKey, "InPlayMode");
                        EditorApplication.update += WaitFramesThenRun;
                    }
                    break;
                case "InPlayMode":
                    if (EditorApplication.isPlaying) EditorApplication.update += WaitFramesThenRun;
                    break;
                case "Done":
                    Debug.Log(SentinelLog);
                    EditorApplication.delayCall += SelfDestruct;
                    break;
            }
        }

        private static int _frameCount = 0;
        private static void WaitFramesThenRun()
        {
            _frameCount++;
            if (_frameCount < WaitFrames) return;
            EditorApplication.update -= WaitFramesThenRun;

            var player = GameObject.FindObjectOfType<CharacterController>();
            bool grounded = player != null && player.isGrounded;
            float y = player != null ? player.transform.position.y : -999;
            
            SessionState.SetString(ResultKey, JsonUtility.ToJson(new { success = true, isGrounded = grounded, y = y }));
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        private static void SelfDestruct()
        {
            string scriptPath = SessionState.GetString(ScriptPathKey, "");
            if (!string.IsNullOrEmpty(scriptPath)) AssetDatabase.DeleteAsset(scriptPath);
            SessionState.EraseString(StateKey); SessionState.EraseString(ScriptPathKey);
        }
    }
}
