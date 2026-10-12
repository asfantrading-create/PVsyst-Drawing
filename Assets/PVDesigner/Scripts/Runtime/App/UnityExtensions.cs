using UnityEngine;

namespace PVDesigner.App
{
    public static class UnityExtensions
    {
        /// <summary>
        /// Returns the existing component or adds one. Do not use "GetComponent() ?? AddComponent()":
        /// in the Editor a missing component is a "fake null" object that the ?? operator does not treat as null.
        /// </summary>
        public static T GetOrAdd<T>(this GameObject go) where T : Component =>
            go.TryGetComponent(out T c) ? c : go.AddComponent<T>();
    }
}
