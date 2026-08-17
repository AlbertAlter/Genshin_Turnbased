// 文件路径：Assets/Scripts/Core/Singleton.cs
using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    [SerializeField] protected bool dontDestroyOnLoad = true;

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this as T;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        Init();  // 调用子类可重写的初始化
    }

    /// <summary>
    /// 子类重写此方法替代 Awake（此时 Instance 已就绪）
    /// </summary>
    protected virtual void Init() { }
}