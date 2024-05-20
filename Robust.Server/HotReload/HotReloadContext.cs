using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Robust.Server.HotReload.Helpers;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Robust.Server.HotReload;

/// <summary>
/// Manages the hot reload context for UI controls.
/// </summary>
/// Adapted from https://github.com/Kir-Antipov/HotAvalonia/blob/master/src/HotAvalonia/AvaloniaHotReloadContext.cs
public sealed class HotReloadContext : IDisposable
{
    [Dependency] private readonly ILogManager _logManager = default!;

    private readonly ISawmill _sawmill;


    /// <summary>
    /// The control managers, mapped by their respective file paths.
    /// </summary>
    private readonly Dictionary<string, ControlManager> _controls;

    /// <summary>
    /// The file watcher responsible for observing changes in control files.
    /// </summary>
    private readonly FileWatcher _watcher;

    /// <summary>
    /// Indicates whether the hot reload is currently enabled.
    /// </summary>
    private bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotReloadContext"/> class.
    /// </summary>
    /// <param name="rootPath">The root directory of the project to watch.</param>
    /// <param name="controls">The list of controls to manage.</param>
    private HotReloadContext(string rootPath, IEnumerable<ControlInfo> controls)
    {
        _ = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _ = Directory.Exists(rootPath) ? rootPath : throw new DirectoryNotFoundException(rootPath);

        _sawmill = _logManager.GetSawmill("hotreload");

        rootPath = Path.GetFullPath(rootPath);
        _controls = controls
            .Select(x => ResolveControlManager(x, rootPath))
            .ToDictionary(static x => x.FileName, FileHelper.FileNameComparer);

        _watcher = new(rootPath, _controls.Keys);
        _watcher.Changed += OnChanged;
        _watcher.Moved += OnMoved;
        _watcher.Error += OnError;
    }

    /// <summary>
    /// Creates a hot reload context using the provided assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing controls.</param>
    /// <param name="rootPath">The root directory of the project.</param>
    /// <returns>A new instance of the <see cref="HotReloadContext"/> class.</returns>
    public static HotReloadContext FromAssembly(Assembly assembly, string rootPath)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        return new(rootPath, RuntimeXamlScanner.FindControls(assembly));
    }

    /// <summary>
    /// Creates a hot reload context using the provided control and its file path.
    /// </summary>
    /// <param name="control">The control belonging to the project that needs to be managed.</param>
    /// <param name="controlPath">The full file path that leads to the XAML file defining the control.</param>
    /// <returns>A new instance of the <see cref="HotReloadContext"/> class.</returns>
    public static HotReloadContext FromControl(object control, string controlPath)
    {
        _ = control ?? throw new ArgumentNullException(nameof(control));
        _ = controlPath ?? throw new ArgumentNullException(nameof(controlPath));
        _ = File.Exists(controlPath) ? controlPath : throw new FileNotFoundException(controlPath);

        controlPath = Path.GetFullPath(controlPath);
        if (!RuntimeXamlScanner.TryExtractControlUri(control.GetType(), out string? controlUri))
            throw new ArgumentException(
                "The provided control is not a valid user-defined control. Could not determine its URI.",
                nameof(control));

        string rootPath = UriHelper.ResolveHostPath(controlUri, controlPath);
        return FromAssembly(control.GetType().Assembly, rootPath);
    }

    /// <summary>
    /// Indicates whether the hot reload is currently enabled.
    /// </summary>
    public bool IsHotReloadEnabled => _enabled;

    /// <summary>
    /// Enables the hot reload feature.
    /// </summary>
    public void EnableHotReload() => _enabled = true;

    /// <summary>
    /// Disables the hot reload feature.
    /// </summary>
    public void DisableHotReload() => _enabled = false;

    /// <summary>
    /// Handles the file changes by attempting to reload the corresponding Avalonia control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing details of the changed file.</param>
    private async void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!_enabled)
            return;

        string path = Path.GetFullPath(args.FullPath);
        if (!_controls.TryGetValue(path, out ControlManager? controlManager))
            return;

        try
        {
            await controlManager.ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to reload {Type} ({Uri}): {Error}",
                controlManager.Control.ControlType,
                controlManager.Control.Uri,
                e);
        }
    }

    /// <summary>
    /// Handles the moved files by updating their corresponding <see cref="AvaloniaControlManager"/> entries.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing details of the moved file.</param>
    private void OnMoved(object sender, MovedEventArgs args)
    {
        string oldFullPath = Path.GetFullPath(args.OldFullPath);
        if (!_controls.TryGetValue(oldFullPath, out ControlManager? controlManager))
            return;

        _controls.Remove(oldFullPath);

        controlManager.FileName = Path.GetFullPath(args.FullPath);
        _controls[controlManager.FileName] = controlManager;
    }

    /// <summary>
    /// Handles errors that occur during file monitoring.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing the error details.</param>
    private void OnError(object sender, ErrorEventArgs args)
        => _sawmill.Error("{sender}: An unexpected error occurred while monitoring file changes: {Error}",
            sender,
            args.GetException());

    /// <summary>
    /// Disposes the resources used by this context, effectively disabling the hot reload.
    /// </summary>
    public void Dispose()
    {
        _enabled = false;

        _watcher.Changed -= OnChanged;
        _watcher.Moved -= OnMoved;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

    /// <summary>
    /// Resolves the control manager for the given Avalonia control.
    /// </summary>
    /// <param name="controlInfo">The information about the Avalonia control.</param>
    /// <param name="rootPath">The root directory of the Avalonia project.</param>
    /// <returns>The resolved <see cref="ControlManager"/>.</returns>
    private static ControlManager ResolveControlManager(ControlInfo controlInfo, string rootPath)
    {
        string fileName = Path.GetFullPath(UriHelper.ResolvePathFromUri(rootPath, controlInfo.Uri));
        return new(controlInfo, fileName);
    }
}
