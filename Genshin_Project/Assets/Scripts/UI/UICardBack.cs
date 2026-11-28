using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 公共卡背资源会话。宿主显式准备，Image、翻牌或碎散组件借用 Sprite；本类不创建布局、不读写存档。
/// 同一页面的多张卡共用一个会话。先停止播放并解绑借用者，再 Release；销毁宿主时 Dispose。
/// </summary>
public sealed class UICardBack : IDisposable
{
    public const string DefaultPath = "art_assets/CardBack/" + CardBackCatalog.DefaultId + ".png";

    public Sprite Sprite { get; private set; }
    public string LoadedPath { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsReady => !IsLoading && Sprite != null;
    public bool UsedFallback => IsReady && requestedPath != LoadedPath;
    public string Error { get; private set; }

    private string requestedPath;
    private int requestVersion;
    private bool disposed;

    /// <summary>
    /// 读取本次展示开始时的外观快照；未绑定或未准备好的来源使用默认卡背。
    /// 不订阅 Changed，避免揭晓过程中途换图；下次展示再次调用即可取得最新选择。
    /// </summary>
    public IEnumerator PrepareFromAppearance(ICardAppearanceSource source)
        => Prepare(source?.IsReady == true ? source.SelectedCardBackPath : null);

    /// <summary>
    /// 从 StreamingAssets 相对路径加载；空路径用默认卡背，所选图片失败时回退默认。
    /// 相同请求复用已加载资源（包括成功的默认回退）；新请求使旧请求失效。
    /// 返回后以 IsReady / Error 判断结果，借用的 Sprite 只在下一次 Prepare 或 Release 前有效。
    /// 宿主负责运行、停止此协程，取消后须调用 Release 使迟到回调失效。
    /// </summary>
    public IEnumerator Prepare(string relativePath = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(UICardBack));
        string path = string.IsNullOrWhiteSpace(relativePath) ? DefaultPath : relativePath.Trim().Replace('\\', '/');
        if (IsReady && requestedPath == path) yield break;

        Release();
        int version = requestVersion;
        requestedPath = path;
        IsLoading = true;
        try
        {
            yield return Load(path, version);
            if (version != requestVersion) yield break;
            if (Sprite == null && path != DefaultPath)
                yield return Load(DefaultPath, version);
        }
        finally
        {
            if (version == requestVersion) IsLoading = false;
        }
    }

    /// <summary>为 Image、自定义网格等展示提供同一张借用图片；调用方不得销毁它。</summary>
    public bool TryGetSprite(out Sprite sprite)
    {
        sprite = IsReady ? Sprite : null;
        return sprite != null;
    }

    private IEnumerator Load(string path, int version)
    {
        // 在真正发出子请求前也核对版本，防止被取消的协程稍后继续执行。
        if (version != requestVersion) yield break;
        yield return StreamingAssetSpriteLoader.Load(path, sprite =>
        {
            if (version != requestVersion) { DestroySprite(sprite); return; }
            Sprite = sprite;
            LoadedPath = path;
            Error = null;
        }, error =>
        {
            if (version != requestVersion) return;
            Error = error;
            Debug.LogWarning(error);
        });
    }

    /// <summary>取消当前资源请求并释放自建图片；会话仍可再次 Prepare。</summary>
    public void Release()
    {
        requestVersion++;
        IsLoading = false;
        DestroySprite(Sprite);
        Sprite = null;
        requestedPath = LoadedPath = Error = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        Release();
        disposed = true;
    }

    private static void DestroySprite(Sprite sprite)
    {
        if (sprite == null) return;
        UnityEngine.Object.Destroy(sprite.texture);
        UnityEngine.Object.Destroy(sprite);
    }
}
