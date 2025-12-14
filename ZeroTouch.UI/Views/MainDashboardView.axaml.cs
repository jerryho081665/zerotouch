using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using BruTile.Web;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using Mapsui.Widgets;
using Mapsui.Widgets.ScaleBar;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using ZeroTouch.UI.ViewModels;

namespace ZeroTouch.UI.Views
{
    public partial class MainDashboardView : UserControl
    {
        private DispatcherTimer? _navigationTimer;

        private List<MPoint> _interpolatedPath = new List<MPoint>();

        private int _currentStepIndex = 0;

        private MemoryLayer? _routeLayer;
        private MemoryLayer? _vehicleLayer;

        private MapControl? _mapControl;

        private double _currentVehicleAngle = 0;

        public MainDashboardView()
        {
            InitializeComponent();

            // This will run in the designer, providing a preview for the MapView.
            InitializeMapView_ForPreview();

            // Runtime initialization is moved to the Loaded event to avoid issues in the designer.
            this.Loaded += MainDashboardView_Loaded;

            var slider = this.FindControl<Slider>("ProgressSlider");
            if (slider != null)
            {
                slider.AddHandler(PointerPressedEvent, OnSliderDragStarted, RoutingStrategies.Tunnel);
                slider.AddHandler(PointerReleasedEvent, OnSliderDragEnded, RoutingStrategies.Tunnel);

                slider.AddHandler(PointerCaptureLostEvent, OnSliderDragEnded, RoutingStrategies.Tunnel);
            }
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
            if (DataContext is MainDashboardViewModel vm)
            {
                vm.IsUserInteracting = false;
                vm.SeekCommand.Execute(vm.Progress);
            }
        }

        private void InitializeMap()
        {
            _mapControl = this.FindControl<MapControl>("MapControl");
            if (_mapControl == null) return;

            var map = new Map();

            var urlFormatter = new HttpTileSource(
                new BruTile.Predefined.GlobalSphericalMercator(),
                "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
                new[] { "a", "b", "c", "d" },
                name: "CartoDB Voyager"
            );

            map.Layers.Add(new TileLayer(urlFormatter));


            var lonLats = new[]
            {
                // start point
                (120.2846128114305, 22.732236533422288),

                (120.29053110655967, 22.73249458710715),
                (120.29239839952325, 22.732232361349443),
                (120.29172547199514, 22.727218383170385),

                (120.29532144100894, 22.72667258631913),
                (120.29591697595505, 22.72637869480983),
                (120.29629763948877, 22.72595883940386),

                (120.29661829404742, 22.725467958231206),

                // destination
                (120.29775246573561, 22.723400189901515)
            };
            // var lonLats = new[]
            // {
            //     // start point
            //     (120.28471712200883, 22.73226013221393),

            //     (120.29053110655967, 22.73249458710715),
            //     (120.29239839952325, 22.732232361349443),
            //     (120.29172547199514, 22.727218383170385),

            //     (120.29532144100894, 22.72667258631913),
            //     (120.29591697595505, 22.72637869480983),
            //     (120.29629763948877, 22.72595883940386),

            //     (120.29661829404742, 22.725467958231206),

            //     // destination
            //     (120.29775246573561, 22.723400189901515)
            // };

            var originalWaypoints = new List<MPoint>();

            foreach (var (lon, lat) in lonLats)
            {
                var p = SphericalMercator.FromLonLat(lon, lat);
                originalWaypoints.Add(new MPoint(p.x, p.y));
            }

            _interpolatedPath = InterpolatePath(originalWaypoints, stepSize: 1.2);

            _routeLayer = CreateRouteLayer(_interpolatedPath);
            map.Layers.Add(_routeLayer);

            var destLayer = CreateDestinationLayer(_interpolatedPath.Last());
            map.Layers.Add(destLayer);

            _vehicleLayer = CreateVehicleLayer();
            map.Layers.Add(_vehicleLayer);

            // Remove default widgets
            while (map.Widgets.TryDequeue(out _)) { }

            _mapControl.Map = map;

            // Ensure the map is loaded before performing operations
            _mapControl.Loaded += (s, e) =>
            {
                var firstPoint = _interpolatedPath[0];
                map.Navigator.CenterOn(firstPoint);
                map.Navigator.ZoomTo(2.0); // Zoom Level

                StartNavigationSimulation();
            };
        }

        private List<MPoint> InterpolatePath(List<MPoint> waypoints, double stepSize)
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
                for (int j = 0; j < steps; j++)
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

        private MemoryLayer CreateRouteLayer(List<MPoint> pathPoints)
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
                Features = new[] { feature }
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

            double currentSmoothRotation = _mapControl.Map.Navigator.Viewport.Rotation;

            _navigationTimer.Tick += (s, e) =>
            {
                _currentStepIndex++;

                if (_currentStepIndex >= _interpolatedPath.Count)
                {
                    _currentStepIndex = 1;  // Loop back to start
                    currentSmoothRotation = 0;
                    _currentVehicleAngle = 0;
                    _mapControl.Map.Navigator.RotateTo(0, duration: 0);
                }

                var newLocation = _interpolatedPath[_currentStepIndex];

                var prevIndex = Math.Max(0, _currentStepIndex - 1);

                if (prevIndex < 0) prevIndex = 0;

                var prevLocation = _interpolatedPath[prevIndex];

                var dx = newLocation.X - prevLocation.X;
                var dy = newLocation.Y - prevLocation.Y;
                double angleDeg = 0;

                if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
                {
                    var angleRad = Math.Atan2(dy, dx);
                    angleDeg = angleRad * 180.0 / Math.PI;

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

                        _routeLayer.Features = new[] { newRouteFeature };
                        _routeLayer.DataHasChanged();
                    }
                    else
                    {
                        // Final point reached, clear the route
                        _routeLayer.Features = new List<IFeature>();
                        _routeLayer.DataHasChanged();
                    }
                }

                if (_vehicleLayer != null)
                {
                    var arrowPolygon = CreateArrowPolygon(newLocation, _currentVehicleAngle);

                    var newFeature = new GeometryFeature
                    {
                        Geometry = arrowPolygon
                    };

                    var oldFeature = _vehicleLayer.Features.FirstOrDefault();

                    if (oldFeature?.Styles.FirstOrDefault() is IStyle oldStyle)
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

                    _vehicleLayer.Features = new[] { newFeature };
                    _vehicleLayer.DataHasChanged();
                }

                // Center map on the new location
                _mapControl.Map.Navigator.CenterOn(newLocation);

                // Rotate the map slightly for effect
                // _mapControl.Map.Navigator.RotateTo(_mapControl.Map.Navigator.Viewport.Rotation + 0.01);

                _mapControl.RefreshGraphics();
            };

            _navigationTimer.Start();
        }

        private Polygon CreateArrowPolygon(MPoint center, double angleDegrees)
        {
            double scale = 3.0;

            var points = new[]
            {
                new MPoint(0, 15),
                new MPoint(10, -10),
                new MPoint(0, -4),
                new MPoint(-10, -10),
                new MPoint(0, 15)
            };

            double rad = (angleDegrees - 90) * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            var rotatedCoordinates = new Coordinate[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];

                double xScaled = p.X * scale;
                double yScaled = p.Y * scale;

                double xRot = (xScaled * cos) - (yScaled * sin);
                double yRot = (xScaled * sin) + (yScaled * cos);

                rotatedCoordinates[i] = new Coordinate(center.X + xRot, center.Y + yRot);
            }

            return new Polygon(new LinearRing(rotatedCoordinates));
        }

        private void MainDashboardView_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Loaded -= MainDashboardView_Loaded; // Prevent multiple calls
            InitializeMap(); // Initialize the main dashboard map at runtime
        }

        private void InitializeMapView_ForPreview()
        {
            try
            {
                var mapViewMapControl = this.FindControl<MapControl>("MapViewMapControl");
                if (mapViewMapControl != null)
                {
                    var map = new Map();
                    map.Layers.Add(new TileLayer(new HttpTileSource(new BruTile.Predefined.GlobalSphericalMercator(), "https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png", new[] { "a", "b", "c", "d" }, name: "CartoDB Voyager")));

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
                        lonLats = new[]
                        {
                            (0.0,0.0)
                        };
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
            double diff = target - current;

            while (diff > 180) diff -= 360;
            while (diff < -180) diff += 360;

            return current + diff * t;
        }
    }
}
