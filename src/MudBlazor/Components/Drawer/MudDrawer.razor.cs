﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using MudBlazor.Interfaces;
using MudBlazor.Services;
using MudBlazor.State;
using MudBlazor.Utilities;

namespace MudBlazor
{
#nullable enable
    public partial class MudDrawer : MudComponentBase, INavigationEventReceiver, IBrowserViewportObserver, IDisposable
    {
        private double _height;
        private int _disposeCount;
        private readonly ParameterState<bool> _rtlState;
        private readonly ParameterState<bool> _openState;
        private readonly ParameterState<Breakpoint> _breakpointState;
        private readonly ParameterState<DrawerClipMode> _clipModeState;
        private ElementReference _contentRef;
        private bool _closeOnMouseLeave = false;
        private bool _isRendered;
        private bool _fixed = true;
        private bool _initial = true;
        private bool _keepInitialState;
        private Breakpoint _lastUpdatedBreakpoint = Breakpoint.None;

        public MudDrawer()
        {
            using var registerScope = CreateRegisterScope();
            _clipModeState = registerScope.RegisterParameter<DrawerClipMode>(nameof(ClipMode))
                .WithParameter(() => ClipMode)
                .WithChangeHandler(OnClipModeParameterChange);
            _breakpointState = registerScope.RegisterParameter<Breakpoint>(nameof(Breakpoint))
                .WithParameter(() => Breakpoint)
                .WithChangeHandler(OnBreakpointParameterChangedAsync);
            _openState = registerScope.RegisterParameter<bool>(nameof(Open))
                .WithParameter(() => Open)
                .WithEventCallback(() => OpenChanged)
                .WithChangeHandler(OnOpenParameterChangedAsync);
            _rtlState = registerScope.RegisterParameter<bool>(nameof(RightToLeft))
                .WithParameter(() => RightToLeft)
                .WithChangeHandler(OnRightToLeftParameterChanged);
        }

        private bool OverlayVisible => _openState.Value && Overlay && (Variant == DrawerVariant.Temporary || (IsBelowCurrentBreakpoint() && IsResponsiveOrMini()));

        protected string Classname =>
            new CssBuilder("mud-drawer")
                .AddClass($"mud-drawer-fixed", Fixed)
                .AddClass($"mud-drawer-pos-{GetPosition()}")
                .AddClass($"mud-drawer--open", _openState.Value)
                .AddClass($"mud-drawer--closed", !_openState.Value)
                .AddClass($"mud-drawer--initial", _initial)
                .AddClass($"mud-drawer-{_breakpointState.Value.ToDescriptionString()}")
                .AddClass($"mud-drawer-clipped-{_clipModeState.Value.ToDescriptionString()}")
                .AddClass($"mud-theme-{Color.ToDescriptionString()}", Color != Color.Default)
                .AddClass($"mud-elevation-{Elevation}")
                .AddClass($"mud-drawer-{Variant.ToDescriptionString()}")
                .AddClass(Class)
                .Build();

        protected string OverlayClass =>
            new CssBuilder("mud-drawer-overlay mud-overlay-drawer")
                .AddClass($"mud-drawer-pos-{GetPosition()}")
                .AddClass($"mud-drawer-overlay--open", _openState.Value)
                .AddClass($"mud-drawer-overlay-{Variant.ToDescriptionString()}")
                .AddClass($"mud-drawer-overlay-{_breakpointState.Value.ToDescriptionString()}")
                .AddClass($"mud-drawer-overlay--initial", _initial)
                .Build();

        protected string Stylename =>
            new StyleBuilder()
                .AddStyle("--mud-drawer-width", Width, !string.IsNullOrWhiteSpace(Width) && (!Fixed || Variant == DrawerVariant.Temporary))
                .AddStyle("height", Height, !string.IsNullOrWhiteSpace(Height))
                .AddStyle("--mud-drawer-content-height", string.IsNullOrWhiteSpace(Height) ? _height.ToPx() : Height, Anchor == Anchor.Bottom || Anchor == Anchor.Top)
                .AddStyle("visibility", "hidden", string.IsNullOrWhiteSpace(Height) && _height == 0 && Anchor is Anchor.Bottom or Anchor.Top)
                .AddStyle(Style)
                .Build();

        [Inject]
        protected IBrowserViewportService BrowserViewportService { get; set; } = null!;

        [CascadingParameter]
        private MudDrawerContainer? DrawerContainer { get; set; }

        [CascadingParameter(Name = "RightToLeft")]
        private bool RightToLeft { get; set; }

        /// <summary>
        /// If true, drawer position will be fixed. (CSS position: fixed;)
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public bool Fixed
        {
            get => _fixed && DrawerContainer is MudLayout;
            set
            {
                _fixed = value;
            }
        }

        /// <summary>
        /// The higher the number, the heavier the drop-shadow. 0 for no shadow.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Appearance)]
        public int Elevation { set; get; } = 1;

        /// <summary>
        /// Side from which the drawer will appear.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public Anchor Anchor { get; set; } = Anchor.Start;

        /// <summary>
        /// The color of the component. It supports the theme colors.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Appearance)]
        public Color Color { get; set; } = Color.Default;

        /// <summary>
        /// Variant of the drawer. It specifies how the drawer will be displayed.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public DrawerVariant Variant { get; set; } = DrawerVariant.Responsive;

        /// <summary>
        /// Child content of component.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public RenderFragment? ChildContent { get; set; }

        /// <summary>
        /// Show overlay for responsive and temporary drawers.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public bool Overlay { get; set; } = true;

        /// <summary>
        /// Open drawer automatically on mouse enter when <see cref="Variant" /> parameter is set to <see cref="DrawerVariant.Mini" />.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public bool OpenMiniOnHover { get; set; }

        /// <summary>
        /// Switching point for responsive drawers
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public Breakpoint Breakpoint { get; set; } = Breakpoint.Md;

        /// <summary>
        /// Sets the opened state on the drawer. Can be used with two-way binding to close itself on navigation.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public bool Open { get; set; }

        [Parameter]
        public EventCallback<bool> OpenChanged { get; set; }

        /// <summary>
        /// Width of left/right drawer. Only for non-fixed drawers.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Appearance)]
        public string? Width { get; set; }

        /// <summary>
        /// Width of left/right drawer. Only for non-fixed drawers.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Appearance)]
        public string? MiniWidth { get; set; }

        /// <summary>
        /// Height of top/bottom temporary drawer
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Appearance)]
        public string? Height { get; set; }

        /// <summary>
        /// Specify how the drawer should behave inside a MudLayout. It affects the position relative to <see cref="MudAppBar"/>.
        /// </summary>
        [Parameter]
        [Category(CategoryTypes.Drawer.Behavior)]
        public DrawerClipMode ClipMode { get; set; }

        protected override void OnInitialized()
        {
            if (Variant != DrawerVariant.Temporary)
            {
                DrawerContainer?.Add(this);
            }
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await UpdateHeightAsync();
                await BrowserViewportService.SubscribeAsync(this, fireImmediately: true);

                _isRendered = true;
                if (string.IsNullOrWhiteSpace(Height) && Anchor is Anchor.Bottom or Anchor.Top)
                {
                    StateHasChanged();
                }
            }

            await base.OnAfterRenderAsync(firstRender);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                if (disposing)
                {
                    DrawerContainer?.Remove(this);

                    if (IsJSRuntimeAvailable)
                    {
                        BrowserViewportService.UnsubscribeAsync(this).AndForget();
                    }
                }
            }
        }

        private async Task OnOpenParameterChangedAsync(ParameterChangedEventArgs<bool> arg)
        {
            if (_isRendered && _initial && !_keepInitialState)
            {
                _initial = false;
            }
            if (_keepInitialState)
            {
                _keepInitialState = false;
            }
            if (_isRendered && arg.Value && Anchor is Anchor.Top or Anchor.Bottom)
            {
                await UpdateHeightAsync();
            }

            DrawerContainerUpdate();
        }

        private async Task OnBreakpointParameterChangedAsync(ParameterChangedEventArgs<Breakpoint> arg)
        {
            if (_isRendered)
            {
                await UpdateBreakpointStateAsync(_lastUpdatedBreakpoint);
            }

            DrawerContainerUpdate();
        }

        private void OnClipModeParameterChange()
        {
            if (Fixed)
            {
                DrawerContainerUpdate();
            }

            StateHasChanged();
        }

        private void OnRightToLeftParameterChanged() => DrawerContainerUpdate();

        private void DrawerContainerUpdate() => (DrawerContainer as IMudStateHasChanged)?.StateHasChanged();

        private Task CloseDrawerAsync()
        {
            if (_openState.Value)
            {
                return _openState.SetValueAsync(false);
            }

            return Task.CompletedTask;
        }

        private async Task UpdateHeightAsync()
        {
            _height = (await _contentRef.MudGetBoundingClientRectAsync())?.Height ?? 0;
        }

        private async Task UpdateBreakpointStateAsync(Breakpoint breakpoint)
        {
            if (breakpoint == Breakpoint.None)
            {
                breakpoint = await BrowserViewportService.GetCurrentBreakpointAsync();
            }

            var isStateChanged = false;
            if (ShouldCloseDrawer(breakpoint))
            {
                await _openState.SetValueAsync(false);
                isStateChanged = true;
            }
            else if (ShouldOpenDrawer(breakpoint))
            {
                await _openState.SetValueAsync(true);
                isStateChanged = true;
            }

            _lastUpdatedBreakpoint = breakpoint;
            if (isStateChanged)
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        private bool IsBelowCurrentBreakpoint() => IsBelowBreakpoint(_lastUpdatedBreakpoint);

        private bool IsBelowBreakpoint(Breakpoint breakpoint) => breakpoint < _breakpointState.Value;

        private bool IsResponsiveOrMini() => Variant is DrawerVariant.Responsive or DrawerVariant.Mini;

        private bool ShouldCloseDrawer(Breakpoint breakpoint) => IsBelowBreakpoint(breakpoint) && !IsBelowCurrentBreakpoint() && IsResponsiveOrMini();

        private bool ShouldOpenDrawer(Breakpoint breakpoint) => !IsBelowBreakpoint(breakpoint) && IsBelowCurrentBreakpoint() && IsResponsiveOrMini();

        internal string GetPosition()
        {
            return Anchor switch
            {
                Anchor.Start => _rtlState.Value ? "right" : "left",
                Anchor.End => _rtlState.Value ? "left" : "right",
                _ => Anchor.ToDescriptionString()
            };
        }

        private async Task OnMouseEnterAsync()
        {
            if (Variant == DrawerVariant.Mini && !_openState.Value && OpenMiniOnHover)
            {
                _closeOnMouseLeave = true;
                await _openState.SetValueAsync(true);
            }
        }

        private async Task OnMouseLeaveAsync()
        {
            if (Variant == DrawerVariant.Mini && _openState.Value && _closeOnMouseLeave)
            {
                _closeOnMouseLeave = false;
                await _openState.SetValueAsync(false);
            }
        }

        async Task INavigationEventReceiver.OnNavigation()
        {
            if (Variant == DrawerVariant.Temporary ||
                (Variant == DrawerVariant.Responsive && await BrowserViewportService.GetCurrentBreakpointAsync() < _breakpointState.Value))
            {
                await _openState.SetValueAsync(false);
            }
        }

        Guid IBrowserViewportObserver.Id { get; } = Guid.NewGuid();

        ResizeOptions IBrowserViewportObserver.ResizeOptions { get; } = new()
        {
            ReportRate = 50,
            NotifyOnBreakpointOnly = false
        };

        async Task IBrowserViewportObserver.NotifyBrowserViewportChangeAsync(BrowserViewportEventArgs browserViewportEventArgs)
        {
            if (browserViewportEventArgs.IsImmediate)
            {
                var isBelowBreakpoint = IsBelowBreakpoint(browserViewportEventArgs.Breakpoint);
                _lastUpdatedBreakpoint = browserViewportEventArgs.Breakpoint;
                if (isBelowBreakpoint && _openState.Value)
                {
                    _keepInitialState = true;
                    await _openState.SetValueAsync(false);
                }

                return;
            }

            if (!_isRendered)
            {
                return;
            }

            await InvokeAsync(() => UpdateBreakpointStateAsync(browserViewportEventArgs.Breakpoint));
        }
    }
}
