using System;
using System.Runtime.CompilerServices;

namespace KSPCommunityFixes
{
    static class UnityObjectExtensions
    {
        /// <summary>
        /// True if this reference to an UnityEngine.Object is destroyed (or not yet initialized).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDestroyed(this UnityEngine.Object unityObject)
        {
            return unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// True if this reference to an UnityEngine.Object is null.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null);
        }

        /// <summary>
        /// True if this reference to an UnityEngine.Object is null, or if the underlying C++ object is destroyed.<br/>
        /// Equivalent as testing <c>object == null</c> but faster.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrDestroyed(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// Return "null" when the reference to the UnityEngine.Object is null or if the underlying C++ object is destroyed/not initialized.<br/>
        /// Allow using null conditional and null coalescing operators with classes deriving from UnityEngine.Object while keeping the
        /// "a destroyed object is equal to null" Unity concept.<br/>
        /// Example :<br/>
        /// <c>float x = myUnityObject.AsNull()?.myFloatField ?? 0f;</c><br/>
        /// will evaluate to <c>0f</c> when <c>myUnityObject</c> is destroyed, instead of returning the value still
        /// available on the managed instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AsNull<T>(this T unityObject) where T : UnityEngine.Object
        {
            if (ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero)
                return null;

            return unityObject;
        }
    }
}
