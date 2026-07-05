using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustEnoughEditor
{
    [Serializable]
    public class FavoriteFolderItem
    {
        public string guid;
        public Color color = new Color(0, 0, 0, 0); // デフォルトは透明（色なし）
    }

    public class FavoriteFoldersData : ScriptableObject
    {
        [SerializeField]
        public List<FavoriteFolderItem> items = new List<FavoriteFolderItem>();
    }
}
