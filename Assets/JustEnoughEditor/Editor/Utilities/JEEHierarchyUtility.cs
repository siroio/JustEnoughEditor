using System;
using System.Collections.Generic;
using UnityEngine;

namespace JustEnoughEditor
{
    public static class JEEHierarchyUtility
    {
        public static int GetDepth(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            var depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        public static string GetPath(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", names);
        }
    }
}
