using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>使用独立预览场景，只关闭本测试拥有的场景，不保存、卸载或重载其他场景。</summary>
    internal sealed class EditorSceneTestScope : IDisposable
    {
        private readonly Scene _previousActiveScene;
        private readonly UnityEngine.Object[] _previousSelection;
        private readonly List<Scene> _previewScenes = new List<Scene>();
        private bool _disposed;

        public Scene TestScene { get; }

        public EditorSceneTestScope()
        {
            _previousActiveScene = SceneManager.GetActiveScene();
            _previousSelection = Selection.objects;
            try
            {
                // EditMode Test Runner 自己会创建未保存场景，不能再NewScene(Additive)。
                // 预览场景独立存在；对象由测试显式移入，不切换运行器或用户的活动场景。
                TestScene = EditorSceneManager.NewPreviewScene();
                if (!TestScene.IsValid() || !TestScene.isLoaded)
                    throw new InvalidOperationException("无法创建测试专用预览场景。");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Scene OpenPreview(string scenePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EditorSceneTestScope));
            // 项目 Unity 2022.3 的单参数 OpenPreviewScene 是 internal；按确切签名调用，
            // 不能退回 OpenScene(Single) 或复用用户已经打开的正式场景。
            MethodInfo openPreview = typeof(EditorSceneManager).GetMethod("OpenPreviewScene",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            if (openPreview == null)
                throw new MissingMethodException(typeof(EditorSceneManager).FullName, "OpenPreviewScene(string)");
            var scene = (Scene)openPreview.Invoke(null, new object[] { scenePath });
            _previewScenes.Add(scene);
            return scene;
        }

        public static List<T> GetComponents<T>(Scene scene) where T : Component
        {
            var components = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
                components.AddRange(root.GetComponentsInChildren<T>(true));
            return components;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                for (int index = _previewScenes.Count - 1; index >= 0; index--)
                    if (_previewScenes[index].IsValid() && _previewScenes[index].isLoaded)
                        EditorSceneManager.ClosePreviewScene(_previewScenes[index]);
            }
            finally
            {
                try
                {
                    if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
                        SceneManager.SetActiveScene(_previousActiveScene);
                    if (TestScene.IsValid() && TestScene.isLoaded)
                        EditorSceneManager.ClosePreviewScene(TestScene);
                }
                finally
                {
                    _previewScenes.Clear();
                    Selection.objects = _previousSelection;
                }
            }
        }
    }
}
