using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;

namespace Game.WaterSortPuzzle
{
    /// <summary>按主资源包地址取得倒水游戏所需的资源。</summary>
    internal static class WaterSortPuzzleResources
    {
        private const string BOTTLEATLASLOCATION = "Atlas/Bottle"; // 主资源包中的瓶子图集地址。
        private const string BOTTLELOCATION = "Prefabs/UI/Bottle/go_bottle"; // 框架收集器生成的瓶子地址。

        internal static UniTask<IAssetHandle<SpriteAtlas>> LoadBottleAtlasAsync(CancellationToken cancellationToken)
        {
            return ResourceManager.Instance.LoadAssetAsync<SpriteAtlas>(
                BOTTLEATLASLOCATION, cancellationToken: cancellationToken);
        }

        internal static UniTask<IAssetHandle<GameObject>> LoadBottlePrefabAsync(CancellationToken cancellationToken)
        {
            return ResourceManager.Instance.LoadAssetAsync<GameObject>(
                BOTTLELOCATION, cancellationToken: cancellationToken);
        }
    }
}
