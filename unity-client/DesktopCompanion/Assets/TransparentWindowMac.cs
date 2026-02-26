using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Makes the Unity window transparent, borderless, and always-on-top on macOS.
/// Uses Objective-C runtime calls via P/Invoke — no native plugin needed.
/// Only activates in BUILDS, not in the Unity Editor.
/// </summary>
public class TransparentWindowMac : MonoBehaviour
{
    [Header("Overlay Settings")]
    public bool alwaysOnTop = true;
    public bool borderless = true;
    public bool transparentBackground = true;
    public bool removeShadow = true;

    // ─── Objective-C Runtime P/Invoke ───────────────────────────────
    [DllImport("/usr/lib/libobjc.dylib")]
    static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    static extern IntPtr sel_registerName(string selector);

    // objc_msgSend variants for different signatures
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr msgSend_RetPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern void msgSend_Bool(IntPtr receiver, IntPtr selector, bool arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern void msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern void msgSend_Long(IntPtr receiver, IntPtr selector, long arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr msgSend_RetPtr_Long(IntPtr receiver, IntPtr selector, long arg);

    // ─── Lifecycle ──────────────────────────────────────────────────
    IEnumerator Start()
    {
#if UNITY_EDITOR
        Debug.Log("TransparentWindowMac: Skipping in Editor mode.");
        yield break;
#else
        // Wait for the window to be fully created
        yield return new WaitForSeconds(0.5f);
        SetupOverlayWindow();
#endif
    }

    /// <summary>
    /// Re-apply transparency after a resolution change.
    /// Call this from WindowDragger after Screen.SetResolution().
    /// </summary>
    public void ReapplyTransparency()
    {
#if !UNITY_EDITOR
        StartCoroutine(DelayedReapply());
#endif
    }

    IEnumerator DelayedReapply()
    {
        // Give Unity a frame to finish the resolution change
        yield return new WaitForEndOfFrame();
        yield return null;
        SetupOverlayWindow();
    }

    void SetupOverlayWindow()
    {
        IntPtr window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            Debug.LogError("TransparentWindowMac: Could not find the main window!");
            return;
        }

        if (transparentBackground)
        {
            // [window setOpaque:NO]
            msgSend_Bool(window, sel_registerName("setOpaque:"), false);

            // [window setBackgroundColor:[NSColor clearColor]]
            IntPtr nsColor = objc_getClass("NSColor");
            IntPtr clearColor = msgSend_RetPtr(nsColor, sel_registerName("clearColor"));
            msgSend_IntPtr(window, sel_registerName("setBackgroundColor:"), clearColor);

            // CRITICAL: Make the content view and its Metal layer transparent too
            // NSView *contentView = [window contentView]
            IntPtr contentView = msgSend_RetPtr(window, sel_registerName("contentView"));
            if (contentView != IntPtr.Zero)
            {
                // CALayer *layer = [contentView layer]
                IntPtr layer = msgSend_RetPtr(contentView, sel_registerName("layer"));
                if (layer != IntPtr.Zero)
                {
                    // [layer setOpaque:NO]
                    msgSend_Bool(layer, sel_registerName("setOpaque:"), false);
                    Debug.Log("TransparentWindowMac: Metal layer set to non-opaque.");
                }

                // Also try to set wantsLayer and make the view itself transparent
                // [contentView setWantsLayer:YES]
                msgSend_Bool(contentView, sel_registerName("setWantsLayer:"), true);

                // Walk sublayers and set them non-opaque too
                if (layer != IntPtr.Zero)
                {
                    IntPtr sublayers = msgSend_RetPtr(layer, sel_registerName("sublayers"));
                    if (sublayers != IntPtr.Zero)
                    {
                        IntPtr count = msgSend_RetPtr(sublayers, sel_registerName("count"));
                        long sublayerCount = count.ToInt64();
                        for (long i = 0; i < sublayerCount; i++)
                        {
                            IntPtr sublayer = msgSend_RetPtr_Long(sublayers, sel_registerName("objectAtIndex:"), i);
                            if (sublayer != IntPtr.Zero)
                            {
                                msgSend_Bool(sublayer, sel_registerName("setOpaque:"), false);
                            }
                        }
                        Debug.Log($"TransparentWindowMac: Set {sublayerCount} sublayer(s) to non-opaque.");
                    }
                }
            }

            // Set the window's color space to support alpha
            IntPtr nsColorSpace = objc_getClass("NSColorSpace");
            IntPtr srgbSpace = msgSend_RetPtr(nsColorSpace, sel_registerName("sRGBColorSpace"));
            if (srgbSpace != IntPtr.Zero)
            {
                msgSend_IntPtr(window, sel_registerName("setColorSpace:"), srgbSpace);
            }

            Debug.Log("TransparentWindowMac: Background set to transparent.");
        }

        if (borderless)
        {
            // [window setStyleMask:NSWindowStyleMaskBorderless] = 0
            // But keep resizable bit so we can interact: just use 0 for fully borderless
            msgSend_Long(window, sel_registerName("setStyleMask:"), 0);
            Debug.Log("TransparentWindowMac: Window is now borderless.");
        }

        if (alwaysOnTop)
        {
            // [window setLevel:NSFloatingWindowLevel] = 3
            // Use 3 for floating, or 24 for screen saver level (above everything)
            msgSend_Long(window, sel_registerName("setLevel:"), 3);
            Debug.Log("TransparentWindowMac: Window set to always-on-top.");
        }

        if (removeShadow)
        {
            // [window setHasShadow:NO]
            msgSend_Bool(window, sel_registerName("setHasShadow:"), false);
        }

        // Make sure the window can receive mouse events
        // [window setIgnoresMouseEvents:NO]
        msgSend_Bool(window, sel_registerName("setIgnoresMouseEvents:"), false);

        // [window setAcceptsMouseMovedEvents:YES]
        msgSend_Bool(window, sel_registerName("setAcceptsMouseMovedEvents:"), true);

        Debug.Log("TransparentWindowMac: Overlay setup complete!");
    }

    IntPtr GetMainWindow()
    {
        // NSApplication.sharedApplication
        IntPtr nsAppClass = objc_getClass("NSApplication");
        IntPtr nsApp = msgSend_RetPtr(nsAppClass, sel_registerName("sharedApplication"));

        // Try [NSApp mainWindow] first
        IntPtr window = msgSend_RetPtr(nsApp, sel_registerName("mainWindow"));
        if (window != IntPtr.Zero) return window;

        // Fallback: [NSApp keyWindow]
        window = msgSend_RetPtr(nsApp, sel_registerName("keyWindow"));
        if (window != IntPtr.Zero) return window;

        // Fallback: first window from [NSApp windows]
        IntPtr windowsArray = msgSend_RetPtr(nsApp, sel_registerName("windows"));
        if (windowsArray != IntPtr.Zero)
        {
            window = msgSend_RetPtr_Long(windowsArray, sel_registerName("objectAtIndex:"), 0);
        }

        return window;
    }
}
