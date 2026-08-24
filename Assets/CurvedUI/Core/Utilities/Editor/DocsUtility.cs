using System;
using UnityEditor;

namespace CurvedUI.Core.Utilities.Editor
{
    public static class DocsUtility
    {
        private const string DocsURL = "https://superstatic.gitbook.io/curvedui-docs/";
        
        public static void OpenDocs(Bookmark bookmark)
        {
            switch (bookmark)
            {
                case Bookmark.General:Help.BrowseURL(DocsURL); break;
                case Bookmark.UnityXR:Help.BrowseURL(DocsURL+"getting-started/setup-for-unity-xr-sdk"); break;
                case Bookmark.SteamVR:Help.BrowseURL(DocsURL+"getting-started/setup-for-steam-vr-sdk"); break;
                case Bookmark.MetaXR:Help.BrowseURL(DocsURL+"getting-started/setup-for-meta-xr-sdk"); break;
                case Bookmark.CustomRay:Help.BrowseURL(DocsURL+"getting-started/setup-for-other-platforms"); break;
                default: throw new ArgumentOutOfRangeException(nameof(bookmark), bookmark, null);
            }
        }
        
        public enum Bookmark
        {
            General = 0,
            UnityXR = 1,
            CustomRay = 2,
            SteamVR = 3,
            MetaXR = 4,
        }
    }
}