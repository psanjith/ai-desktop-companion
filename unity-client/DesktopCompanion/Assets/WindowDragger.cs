using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Controls for the desktop companion:
/// - Click & drag anywhere: move the window (uses macOS native drag)
/// - Scroll wheel / +/- keys: resize the entire window
/// - ESC: quit
/// </summary>
public class WindowDragger : MonoBehaviour
{
    [Header("Window Size")]
    public int sizeStep = 80;       // Pixels to grow/shrink per step
    public int minSize = 300;
    public int maxSize = 1200;

    private int currentWidth;
    private int currentHeight;
    private TransparentWindowMac transparentWindow;

    // ─── Objective-C Runtime (for window dragging) ──────────────────
#if !UNITY_EDITOR
    [DllImport("/usr/lib/libobjc.dylib")]
    static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    static extern IntPtr sel_registerName(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr msgSend_RetPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern void msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr msgSend_RetPtr_Long(IntPtr receiver, IntPtr selector, long arg);

    private IntPtr cachedWindow = IntPtr.Zero;

    IntPtr GetWindow()
    {
        if (cachedWindow != IntPtr.Zero) return cachedWindow;

        IntPtr nsAppClass = objc_getClass("NSApplication");
        IntPtr nsApp = msgSend_RetPtr(nsAppClass, sel_registerName("sharedApplication"));

        IntPtr window = msgSend_RetPtr(nsApp, sel_registerName("mainWindow"));
        if (window != IntPtr.Zero) { cachedWindow = window; return window; }

        window = msgSend_RetPtr(nsApp, sel_registerName("keyWindow"));
        if (window != IntPtr.Zero) { cachedWindow = window; return window; }

        IntPtr windowsArray = msgSend_RetPtr(nsApp, sel_registerName("windows"));
        if (windowsArray != IntPtr.Zero)
            window = msgSend_RetPtr_Long(windowsArray, sel_registerName("objectAtIndex:"), 0);

        cachedWindow = window;
        return window;
    }

    /// <summary>
    /// Tell macOS to handle the window drag natively.
    /// Uses [NSWindow performWindowDragWithEvent:] — no NSPoint marshaling needed!
    /// </summary>
    void StartNativeDrag()
    {
        IntPtr window = GetWindow();
        if (window == IntPtr.Zero) return;

        IntPtr nsAppClass = objc_getClass("NSApplication");
        IntPtr nsApp = msgSend_RetPtr(nsAppClass, sel_registerName("sharedApplication"));
        IntPtr currentEvent = msgSend_RetPtr(nsApp, sel_registerName("currentEvent"));

        if (currentEvent != IntPtr.Zero)
        {
            msgSend_IntPtr(window, sel_registerName("performWindowDragWithEvent:"), currentEvent);
        }
    }
#endif

    IEnumerator Start()
    {
        transparentWindow = FindObjectOfType<TransparentWindowMac>();

        // Load saved window size
        currentWidth = PlayerPrefs.GetInt("WindowWidth", Screen.width);
        currentHeight = PlayerPrefs.GetInt("WindowHeight", Screen.height);

        // Wait for TransparentWindowMac to finish its setup first (it waits 0.5s)
        yield return new WaitForSeconds(1.0f);

        // Only apply saved size if it differs from current
        if (currentWidth != Screen.width || currentHeight != Screen.height)
        {
            Screen.SetResolution(currentWidth, currentHeight, false);
            if (transparentWindow != null)
                transparentWindow.ReapplyTransparency();
        }
    }

    void Update()
    {
    // ─── Window Drag (click & drag anywhere EXCEPT interactive UI) ───
#if !UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            // Don't steal clicks from buttons, input fields, etc.
            if (!IsPointerOverInteractable())
                StartNativeDrag();
        }
#endif

        // ─── Window Resize ───
        bool sizeUp = false;
        bool sizeDown = false;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f) sizeUp = true;
        else if (scroll < 0f) sizeDown = true;

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            sizeUp = true;
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            sizeDown = true;

        if (sizeUp)
            ResizeWindow(sizeStep);
        else if (sizeDown)
            ResizeWindow(-sizeStep);

        // ESC to quit
        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();
    }

    // Reused list to avoid GC allocs on every frame
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();

    /// <summary>
    /// Returns true when the cursor is over a Selectable UI element (Button, InputField,
    /// Dropdown, etc.) so drag does not steal those interactions.
    /// </summary>
    private bool IsPointerOverInteractable()
    {
        if (EventSystem.current == null) return false;
        _raycastResults.Clear();
        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        EventSystem.current.RaycastAll(data, _raycastResults);
        foreach (var r in _raycastResults)
            if (r.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null)
                return true;
        return false;
    }

    private void ResizeWindow(int delta)
    {
        currentWidth = Mathf.Clamp(currentWidth + delta, minSize, maxSize);
        currentHeight = Mathf.Clamp(currentHeight + delta, minSize, maxSize);
        Screen.SetResolution(currentWidth, currentHeight, false);

        if (transparentWindow != null)
            transparentWindow.ReapplyTransparency();

        PlayerPrefs.SetInt("WindowWidth", currentWidth);
        PlayerPrefs.SetInt("WindowHeight", currentHeight);
        PlayerPrefs.Save();

        Debug.Log($"Window resized to {currentWidth}x{currentHeight}");
    }
}
