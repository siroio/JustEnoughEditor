using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustEnoughEditor
{
    [Serializable]
    public class FavoriteAssetItem
    {
        public string guid;
        public Color color = new(0, 0, 0, 0); // デフォルトは透明（色なし）
    }

    public class FavoriteAssetsData : ScriptableObject
    {
        [SerializeField]
        public List<FavoriteAssetItem> items = new();
    }
}
