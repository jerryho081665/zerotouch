using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using BruTile.Web;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using ZeroTouch.UI.Navigation;
using ZeroTouch.UI.ViewModels;

namespace ZeroTouch.UI.Views
{
    public partial class MainDashboardView : UserControl
    {
        private DispatcherTimer? _navigationTimer;

        private List<MPoint> _interpolatedPath = [];

        private int _currentStepIndex = 0;

        private MemoryLayer? _routeLayer;
        private MemoryLayer? _vehicleLayer;

        private MapControl? _mapControl;

        private double _currentVehicleAngle = 0;

        private MemoryLayer? _destinationLayer;

        public MainDashboardView()
        {
            InitializeComponent();

            // This will run in the designer, providing a preview for the MapView.
            InitializeMapView_ForPreview();

            // Runtime initialization is moved to the Loaded event to avoid issues in the designer.
            Loaded += MainDashboardView_Loaded;

            var slider = this.FindControl<Slider>("ProgressSlider");
            
            if (slider == null)
                return;
            
            slider.AddHandler(PointerPressedEvent, OnSliderDragStarted, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderDragEnded, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerCaptureLostEvent, OnSliderDragEnded, RoutingStrategies.Tunnel);
        }

        private void OnSliderDragStarted(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainDashboardViewModel vm)
            {
                vm.IsUserInteracting = true;
            }
        }

        private void OnSliderDragEnded(object? sender, PointerReleasedEventArgs e)
        {
            if (DataContext is not MainDashboardViewModel vm)
                return;
            
            vm.IsUserInteracting = false;
            vm.SeekCommand.Execute(vm.Progress);
        }

        private void InitializeMap()
        {
            _mapControl = this.FindControl<MapControl>("MapControl");
            if (_mapControl == null) return;

            var map = new Map();

            var urlFormatter = new HttpTileSource(
                new BruTile.Predefined.GlobalSphericalMercator(),
                "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
                ["a", "b", "c", "d"],
                name: "CartoDB Voyager"
            );

            map.Layers.Add(new TileLayer(urlFormatter));

            _routeLayer = new MemoryLayer { Name = "RouteLayer" };
            _vehicleLayer = CreateVehicleLayer();
            _destinationLayer = new MemoryLayer { Name = "DestinationLayer" };

            map.Layers.Add(_routeLayer);
            map.Layers.Add(_destinationLayer);
            map.Layers.Add(_vehicleLayer);

            while (map.Widgets.TryDequeue(out _))
            {
            }

            _mapControl.Map = map;

            _mapControl.Loaded += (s, e) =>
            {
                const double startLon = 120.2846;
                const double startLat = 22.7322;

                var p = SphericalMercator.FromLonLat(startLon, startLat);
                var startPoint = new MPoint(p.x, p.y);

                if (_mapControl?.Map?.Navigator != null)
                {
                    _mapControl.Map.Navigator.CenterOn(startPoint);
                    _mapControl.Map.Navigator.ZoomTo(2.0);
                }

                PreviewRoute("Home");

                _navigationTimer?.Stop();
            };
        }

        private List<MPoint> GetRoutePoints(string routeIdentifier)
        {
            var fileName = routeIdentifier switch
            {
                "Home" => "route-1.json",
                "Work" => "route-2.json",
                "Gym" => "route-3.json",
                "School" => "route-4.json",
                "Cinema" => "route-5.json",
                _ => routeIdentifier.EndsWith(".json") ? routeIdentifier : $"{routeIdentifier}.json"
            };

            var routeUri = new Uri($"avares://ZeroTouch.UI/Assets/Routes/{fileName}");
            var points = new List<MPoint>();

            try
            {
                if (AssetLoader.Exists(routeUri))
                {
                    using var stream = AssetLoader.Open(routeUri);
                    using var reader = new System.IO.StreamReader(stream);
                    var jsonContent = reader.ReadToEnd();
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
                    
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (!element.TryGetProperty("lon", out var lonProp) ||
                            !element.TryGetProperty("lat", out var latProp)) continue;
                        
                        var p = SphericalMercator.FromLonLat(lonProp.GetDouble(), latProp.GetDouble());
                        points.Add(new MPoint(p.x, p.y));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to load route: {ex.Message}");
            }

            return points;
        }

        private void PreviewRoute(string routeIdentifier)
        {
            var originalWaypoints = GetRoutePoints(routeIdentifier);
            if (originalWaypoints.Count < 2) return;

            var previewPath = InterpolatePath(originalWaypoints, stepSize: 1.2);

            var previewMapControl = this.FindControl<MapControl>("MapViewMapControl");
            if (previewMapControl?.Map == null) return;

            previewMapControl.Map.Layers.Clear();

            previewMapControl.Map.Layers.Add(new TileLayer(new HttpTileSource(
                new BruTile.Predefined.GlobalSphericalMercator(),
                "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
                new[] { "a", "b", "c", "d" },
                name: "CartoDB Voyager")));

            var routeLayer = CreateRouteLayer(previewPath);
            previewMapControl.Map.Layers.Add(routeLayer);
            previewMapControl.Map.Layers.Add(CreateDestinationLayer(previewPath.Last()));

            if (routeLayer.Extent != null)
            {
                previewMapControl.Map.Navigator.ZoomToBox(routeLayer.Extent.Grow(200));
            }

            previewMapControl.RefreshGraphics();
        }

        private void StartNavigation(string routeIdentifier)
        {
            _navigationTimer?.Stop();

            var originalWaypoints = GetRoutePoints(routeIdentifier);
            if (originalWaypoints.Count < 2) return;

            _interpolatedPath = InterpolatePath(originalWaypoints, stepSize: 1.2);
            _currentStepIndex = 0;
            _currentVehicleAngle = 0;

            if (_routeLayer != null)
            {
                var newRouteLayer = CreateRouteLayer(_interpolatedPath);
                _routeLayer.Features = newRouteLayer.Features;
                _routeLayer.DataHasChanged();
            }

            if (_destinationLayer != null)
            {
                var newDestLayer = CreateDestinationLayer(_interpolatedPath.Last());
                _destinationLayer.Features = newDestLayer.Features;
                _destinationLayer.DataHasChanged();
            }

            if (_mapControl?.Map?.Navigator != null)
            {
                _mapControl.Map.Navigator.CenterOn(_interpolatedPath[0]);
                _mapControl.Map.Navigator.ZoomTo(2.0);
            }

            StartNavigationSimulation();

            if (DataContext is MainDashboardViewModel vm)
            {
                vm.CurrentPageIndex = 1;
            }
        }

        private void OnGoRouteClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string routeName)
            {
                StartNavigation(routeName);
            }
        }

        private void OnRouteBlockClicked(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: FocusItemViewModel item })
                return;

            if (DataContext is not MainDashboardViewModel vm)
                return;
            
            vm.RouteFocusGroup.SelectItem(item);
                    
            if (!item.IsArmed)
            {
                item.Activate();
            }
        }

        private List<MPoint> InterpolatePath(List<MPoint>? waypoints, double stepSize)
        {
            var result = new List<MPoint>();

            if (waypoints == null || waypoints.Count == 0) return result;

            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                var start = waypoints[i];
                var end = waypoints[i + 1];

                var distance = start.Distance(end);

                // Count steps needed
                var steps = Math.Max(1, (int)(distance / stepSize));

                // Linear interpolation
                for (var j = 0; j < steps; j++)
                {
                    var fraction = (double)j / steps;
                    var x = start.X + (end.X - start.X) * fraction;
                    var y = start.Y + (end.Y - start.Y) * fraction;
                    result.Add(new MPoint(x, y));
                }
            }

            // add the last waypoint
            result.Add(waypoints.Last());

            return result;
        }

        private MemoryLayer CreateRouteLayer(List<MPoint>? pathPoints)
        {
            if (pathPoints == null || pathPoints.Count < 2) return new MemoryLayer { Name = "RouteLayer" };

            var coordinates = pathPoints.Select(p => new Coordinate(p.X, p.Y)).ToArray();

            var lineString = new LineString(coordinates);

            var feature = new GeometryFeature
            {
                Geometry = lineString
            };

            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen(Color.FromArgb(200, 33, 150, 243), 3) // Blue
            });

            return new MemoryLayer
            {
                Name = "RouteLayer",
                Features = [feature]
            };
        }

        private MemoryLayer CreateDestinationLayer(MPoint endPoint)
        {
            var pointFeature = new GeometryFeature
            {
                Geometry = new NetTopologySuite.Geometries.Point(endPoint.X, endPoint.Y)
            };

            pointFeature.Styles.Add(new SymbolStyle
            {
                Fill = new Brush(Color.Gold),
                Outline = new Pen(Color.White, 3),
                SymbolScale = 0.8,
                SymbolType = SymbolType.Ellipse
            });

            return new MemoryLayer { Name = "DestinationLayer", Features = new[] { pointFeature } };
        }

        private MemoryLayer CreateVehicleLayer()
        {
            return new MemoryLayer { Name = "VehicleLayer" };
        }

        private void StartNavigationSimulation()
        {
            if (_mapControl?.Map?.Navigator == null) return;

            _mapControl.Map.Navigator.CenterOn(_interpolatedPath[0]);
            _mapControl.Map.Navigator.ZoomTo(2.0);

            _navigationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };

            var currentSmoothRotation = _mapControl.Map.Navigator.Viewport.Rotation;

            _navigationTimer.Tick += (s, e) =>
            {
                _currentStepIndex++;

                if (_currentStepIndex >= _interpolatedPath.Count)
                {
                    _currentStepIndex = 1; // Loop back to start
                    currentSmoothRotation = 0;
                    _currentVehicleAngle = 0;
                    _mapControl.Map.Navigator.RotateTo(0, duration: 0);
                }

                var newLocation = _interpolatedPath[_currentStepIndex];

                var prevIndex = Math.Max(0, _currentStepIndex - 1);

                var prevLocation = _interpolatedPath[prevIndex];

                var dx = newLocation.X - prevLocation.X;
                var dy = newLocation.Y - prevLocation.Y;

                if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
                {
                    var angleRad = Math.Atan2(dy, dx);
                    var angleDeg = angleRad * 180.0 / Math.PI;

                    var targetRotation = angleDeg - 90;

                    currentSmoothRotation = LerpAngle(currentSmoothRotation, targetRotation, 0.1);

                    _mapControl.Map.Navigator.RotateTo(currentSmoothRotation, duration: 0);

                    _currentVehicleAngle = LerpAngle(_currentVehicleAngle, angleDeg, 0.1);
                }

                if (_routeLayer != null)
                {
                    var remainingPoints = _interpolatedPath.Skip(_currentStepIndex).ToList();

                    if (remainingPoints.Count >= 2)
                    {
                        var coords = remainingPoints.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                        var newLineString = new LineString(coords);

                        var newRouteFeature = new GeometryFeature { Geometry = newLineString };

                        newRouteFeature.Styles.Add(new VectorStyle
                        {
                            Line = new Pen(Color.FromArgb(200, 33, 150, 243), 6)
                        });

                        _routeLayer.Features = [newRouteFeature];
                    }
                    else
                    {
                        // Final point reached, clear the route
                        _routeLayer.Features = new List<IFeature>();
                    }

                    _routeLayer.DataHasChanged();
                }

                if (_vehicleLayer != null)
                {
                    var arrowPolygon = CreateArrowPolygon(newLocation, _currentVehicleAngle);

                    var newFeature = new GeometryFeature
                    {
                        Geometry = arrowPolygon
                    };

                    var oldFeature = _vehicleLayer.Features.FirstOrDefault();

                    if (oldFeature?.Styles.FirstOrDefault() is { } oldStyle)
                    {
                        newFeature.Styles.Add(oldStyle);
                    }
                    else
                    {
                        newFeature.Styles.Add(new VectorStyle
                        {
                            Fill = new Brush(Color.Red),
                            Outline = new Pen(Color.White, 2)
                        });
                    }

                    _vehicleLayer.Features = [newFeature];
                    _vehicleLayer.DataHasChanged();
                }

                // Center map on the new location
                _mapControl.Map.Navigator.CenterOn(newLocation);

                // Rotate the map slightly for effect
                // _mapControl.Map.Navigator.RotateTo(_mapControl.Map.Navigator.Viewport.Rotation + 0.01);

                if (DataContext is MainDashboardViewModel vm)
                {
                    double distToTurn = GetDistanceToNextTurn(_currentStepIndex);
                    double distToDest = CalculateRemainingDistance(_currentStepIndex);

                    if (distToDest < 30)
                    {
                        vm.NavigationDistance = "Arriving";
                        vm.NavigationInstruction = "Destination";
                        vm.NavigationIcon = "●";
                    }
                    else
                    {
                        if (distToTurn > 1000)
                            vm.NavigationDistance = $"in {(distToTurn / 1000.0):F1} km";
                        else
                            vm.NavigationDistance = $"in {(int)distToTurn} m";

                        UpdateTurnInstruction(vm);

                        if (vm.NavigationIcon == "↑")
                        {
                        }
                    }
                }

                _mapControl.RefreshGraphics();
            };

            _navigationTimer.Start();
        }

        private static Polygon CreateArrowPolygon(MPoint center, double angleDegrees)
        {
            const double scale = 3.0;

            var points = new[]
            {
                new MPoint(0, 15),
                new MPoint(10, -10),
                new MPoint(0, -4),
                new MPoint(-10, -10),
                new MPoint(0, 15)
            };

            var rad = (angleDegrees - 90) * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            var rotatedCoordinates = new Coordinate[points.Length];

            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];

                var xScaled = p.X * scale;
                var yScaled = p.Y * scale;

                var xRot = (xScaled * cos) - (yScaled * sin);
                var yRot = (xScaled * sin) + (yScaled * cos);

                rotatedCoordinates[i] = new Coordinate(center.X + xRot, center.Y + yRot);
            }

            return new Polygon(new LinearRing(rotatedCoordinates));
        }

        private void MainDashboardView_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Loaded -= MainDashboardView_Loaded; // Prevent multiple calls
            InitializeMap(); // Initialize the main dashboard map at runtime
            
            if (DataContext is MainDashboardViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainDashboardViewModel.PreviewRouteId))
                    {
                        PreviewRoute(vm.PreviewRouteId);
                    }
                    
                    if (args.PropertyName == nameof(MainDashboardViewModel.NavigationRouteId))
                    {
                        if (!string.IsNullOrEmpty(vm.NavigationRouteId))
                        {
                            StartNavigation(vm.NavigationRouteId);
                        }
                    }
                };
            }
        }

        private void InitializeMapView_ForPreview()
        {
            try
            {
                var mapViewMapControl = this.FindControl<MapControl>("MapViewMapControl");
                if (mapViewMapControl != null)
                {
                    var map = new Map();
                    map.Layers.Add(new TileLayer(new HttpTileSource(new BruTile.Predefined.GlobalSphericalMercator(),
                        "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
                        new[] { "a", "b", "c", "d" }, name: "CartoDB Voyager")));

                    (double, double)[] lonLats;
                    var routeUri = new Uri("avares://ZeroTouch.UI/Assets/Routes/route-1.json");

                    if (AssetLoader.Exists(routeUri))
                    {
                        using var stream = AssetLoader.Open(routeUri);
                        using var reader = new System.IO.StreamReader(stream);
                        var jsonContent = reader.ReadToEnd();

                        using (var doc = System.Text.Json.JsonDocument.Parse(jsonContent))
                        {
                            lonLats = doc.RootElement.EnumerateArray()
                                .Select(p => (p.GetProperty("lon").GetDouble(), p.GetProperty("lat").GetDouble()))
                                .ToArray();
                        }
                    }
                    else
                    {
                        // Fallback to hardcoded data if file not found in designer
                        lonLats =
                        [
                            (0.0, 0.0)
                        ];
                    }

                    var originalWaypoints = new List<MPoint>();
                    foreach (var (lon, lat) in lonLats)
                    {
                        var p = SphericalMercator.FromLonLat(lon, lat);
                        originalWaypoints.Add(new MPoint(p.x, p.y));
                    }

                    var interpolatedPath = InterpolatePath(originalWaypoints, stepSize: 1.2);
                    if (interpolatedPath.Any())
                    {
                        var routeLayer = CreateRouteLayer(interpolatedPath);
                        map.Layers.Add(routeLayer);

                        var destLayer = CreateDestinationLayer(interpolatedPath.Last());
                        map.Layers.Add(destLayer);

                        mapViewMapControl.Map = map;
                        if (routeLayer.Extent is not null)
                        {
                            mapViewMapControl.Map.Navigator.ZoomToBox(routeLayer.Extent.Grow(100));
                        }
                    }
                    else
                    {
                        mapViewMapControl.Map = map;
                        var center = SphericalMercator.FromLonLat(120.29, 22.72);
                        mapViewMapControl.Map.Navigator.CenterOn(new MPoint(center.x, center.y));
                        mapViewMapControl.Map.Navigator.ZoomTo(14);
                    }
                }
            }
            catch
            {
                // Ignore exceptions in designer
            }
        }

        private double LerpAngle(double current, double target, double t)
        {
            var diff = target - current;

            while (diff > 180) diff -= 360;
            while (diff < -180) diff += 360;

            return current + diff * t;
        }

        private double CalculateRemainingDistance(int currentIndex)
        {
            double totalDistance = 0;

            for (int i = currentIndex; i < _interpolatedPath.Count - 1; i++)
            {
                totalDistance += _interpolatedPath[i].Distance(_interpolatedPath[i + 1]);
            }

            return totalDistance;
        }

        private void UpdateTurnInstruction(MainDashboardViewModel vm)
        {
            const int lookAheadSteps = 80;

            if (_currentStepIndex + lookAheadSteps + 1 >= _interpolatedPath.Count)
            {
                vm.NavigationInstruction = "Arriving";
                vm.NavigationIcon = "●";
                return;
            }

            var pNow = _interpolatedPath[_currentStepIndex];
            var pNext = _interpolatedPath[_currentStepIndex + 1];
            double dx1 = pNext.X - pNow.X;
            double dy1 = pNext.Y - pNow.Y;
            double angleCurrent = Math.Atan2(dy1, dx1);

            var pFuture = _interpolatedPath[_currentStepIndex + lookAheadSteps];
            var pFutureNext = _interpolatedPath[_currentStepIndex + lookAheadSteps + 1];
            double dx2 = pFutureNext.X - pFuture.X;
            double dy2 = pFutureNext.Y - pFuture.Y;
            double angleFuture = Math.Atan2(dy2, dx2);

            double diff = angleFuture - angleCurrent;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;

            double turnThreshold = 0.35;

            if (diff > turnThreshold)
            {
                vm.NavigationInstruction = "Turn Left";
                vm.NavigationIcon = "↰";
            }
            else if (diff < -turnThreshold)
            {
                vm.NavigationInstruction = "Turn Right";
                vm.NavigationIcon = "↱";
            }
            else
            {
                vm.NavigationInstruction = "Go Straight";
                vm.NavigationIcon = "↑";
            }
        }

        private double GetDistanceToNextTurn(int currentIndex)
        {
            if (currentIndex >= _interpolatedPath.Count - 1) return 0;

            double accumulatedDistance = 0;

            var pCurrent = _interpolatedPath[currentIndex];
            var pNext = _interpolatedPath[currentIndex + 1];
            double baseAngle = Math.Atan2(pNext.Y - pCurrent.Y, pNext.X - pCurrent.X);

            for (int i = currentIndex; i < _interpolatedPath.Count - 1; i++)
            {
                var p1 = _interpolatedPath[i];
                var p2 = _interpolatedPath[i + 1];

                double segmentDist = p1.Distance(p2);
                accumulatedDistance += segmentDist;

                double scanAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);

                double diff = scanAngle - baseAngle;
                while (diff > Math.PI) diff -= 2 * Math.PI;
                while (diff < -Math.PI) diff += 2 * Math.PI;

                if (Math.Abs(diff) > 0.43)
                {
                    return accumulatedDistance;
                }
            }

            return accumulatedDistance;
        }
    }
}
