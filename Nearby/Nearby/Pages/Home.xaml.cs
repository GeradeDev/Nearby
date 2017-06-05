﻿using FAB.Forms;
using FormsToolkit;
using Microsoft.Azure.Mobile.Analytics;
using Nearby.Controls;
using Nearby.DependencyServices;
using Nearby.Helpers;
using Nearby.Interfaces;
using Nearby.Utils;
using Nearby.viewModel;
using Newtonsoft.Json;
using Plugin.Geolocator;
using Plugin.Permissions;
using Plugin.Permissions.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Xamarin.Forms;
using Xamarin.Forms.Maps;

namespace Nearby.Pages
{
    public partial class Home : ContentPage
    {
        HomeViewModel vm;

        IMyLocation loc;
        public double longitude;
        public double latitude;

        public List<double> latitudes = new List<double>();
        public List<double> longitudes = new List<double>();
        

        public Home()
        {
            InitializeComponent();

            BindingContext = vm = new HomeViewModel();

            var tbItemNavigateEvents = new ToolbarItem() { Icon = "today" };
            var tbItemNavigateFav = new ToolbarItem() { Icon = "favorite"};
            var tbItemNavigateOptions = new ToolbarItem() { Icon = "settings_cog"};

            //Navigate to nearby page
            tbItemNavigateEvents.Command = new Command(async () =>
            {
                var nav = Application.Current?.MainPage?.Navigation;
                if (nav == null)
                    return;

                if (vm.IsBusy)
                    return;

                await Navigation.PushAsync(new NearbyEvents());
            });

            //Navigate to favs page
            tbItemNavigateFav.Command = new Command(async () =>
            {
                var nav = Application.Current?.MainPage?.Navigation;
                if (nav == null)
                    return;
                await Navigation.PushAsync(new Favourites());
            });

            //Navigate to options page
            tbItemNavigateOptions.Command = new Command(async () =>
            {
                var nav = Application.Current?.MainPage?.Navigation;
                if (nav == null)
                    return;

                if (vm.IsBusy)
                    return;

                await Navigation.PushAsync(new MainMenu());
            });

            if (Device.OS != TargetPlatform.Android)
            {
                ToolbarItems.Add(tbItemNavigateEvents);
                ToolbarItems.Add(tbItemNavigateOptions);

                NavigationPage.SetBackButtonTitle(this, "");
            }
            else
            {
                ToolbarItems.Add(tbItemNavigateEvents);
                ToolbarItems.Add(tbItemNavigateFav);
                ToolbarItems.Add(tbItemNavigateOptions);
            }

            AddSearchButtons(); 
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            vm.UpdateItems();

            MoveToCurrentLocation();
        }

        async Task MoveToCurrentLocation()
        {
            if (IsBusy)
                return;

            IsBusy = true;

            try
            {
                if (placesMap.Pins.Count() == 0)
                {
                    var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permission.Location);

                    if (status != PermissionStatus.Granted)
                    {
                        var results = await CrossPermissions.Current.RequestPermissionsAsync(new[] { Permission.Location });
                        status = results[Permission.Location];
                    }

                    if (status == PermissionStatus.Granted)
                    {
                        Plugin.Geolocator.Abstractions.Position position = new Plugin.Geolocator.Abstractions.Position();

                        if (!Settings.Current.CustomLocationEnabled)
                        {
                            //Get the users current location
                            var locator = CrossGeolocator.Current;
                            position = await locator.GetPositionAsync(10000);

                            var pin = new Pin
                            {
                                Type = PinType.Place,
                                Label = "This is you!",
                                Position = new Position(position.Latitude, position.Longitude)
                            };

                            latitudes.Add(position.Latitude);
                            longitudes.Add(position.Longitude);

                            placesMap.Pins.Add(pin);
                        }
                        else
                        {
                            if (Settings.Current.CustomLatitude == "" || Settings.Current.CustomLongitude == "")
                                Application.Current?.MainPage.DisplayAlert("Location", "Please set a custom location, Or turn off the custom location option on the settings page.", "Got it!");
                            else
                            {
                                position = new Plugin.Geolocator.Abstractions.Position
                                {
                                    Latitude = Convert.ToDouble(Settings.Current.CustomLatitude),
                                    Longitude = Convert.ToDouble(Settings.Current.CustomLongitude)
                                };

                                latitudes.Add(position.Latitude);
                                longitudes.Add(position.Longitude);

                                var pin = new Pin
                                {
                                    Type = PinType.SavedPin,
                                    Label = "This is you!",
                                    Position = new Position(position.Latitude, position.Longitude)
                                };

                                placesMap.Pins.Add(pin);
                            }
                        }

                        if (placesMap.Pins.Count == 1)
                            placesMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Position(position.Latitude, position.Longitude), Distance.FromMiles(0.5)));
                        else
                        {
                            double lowestLat = latitudes.Min();
                            double highestLat = latitudes.Max();
                            double lowestLong = longitudes.Min();
                            double highestLong = longitudes.Max();
                            double finalLat = (lowestLat + highestLat) / 2;
                            double finalLong = (lowestLong + highestLong) / 2;
                            double distance = DistanceCalculation.GeoCodeCalc.CalcDistance(lowestLat, lowestLong, highestLat, highestLong, DistanceCalculation.GeoCodeCalcMeasurement.Kilometers);

                            placesMap.MoveToRegion(MapSpan.FromCenterAndRadius(new Position(finalLat, finalLong), Distance.FromKilometers(distance)));
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            IsBusy = false;
        }

        async Task SearchForPlacesNearby()
        {
            await vm.SearchNearby("");

            try
            {
                placesMap.Pins.Clear();

                MoveToCurrentLocation();

                foreach (var pn in vm.PlacesNearby)
                {
                    var newposition = new Xamarin.Forms.Maps.Position(pn.geometry.location.lat, pn.geometry.location.lng);

                    var pin = new Pin
                    {
                        Type = PinType.Place,
                        Position = newposition,
                        Label = pn.name,
                        Address = pn.vicinity
                    };

                    latitudes.Add(pin.Position.Latitude);
                    longitudes.Add(pin.Position.Longitude);

                    pin.Clicked += async (sender, e) =>
                    {
                        if (Settings.Current.IsConnected)
                        {
                            await Navigation.PushAsync(new PlaceDetailView(pn));
                        }
                        else
                        {
                            MessagingService.Current.SendMessage<MessagingServiceAlert>(MessageKeys.Message, new MessagingServiceAlert { Title = "Offline", Message = "Oh snap! you have gone offline. Please check your internet connection.", Cancel = "Ok" });
                        }
                    };

                    placesMap.Pins.Add(pin);
                }
            }
            catch (Exception ex)
            {

            }
        }

        async Task ToggleRefineOptions()
        {
            await Navigation.PushAsync(new SearchFilters());
        }
        
        async Task AddSearchButtons()
        {
            if(Device.OS == TargetPlatform.Android)
            {
                SearchButton.Children.Add(new FloatingActionButtonView
                {
                    Size = FloatingActionButtonSize.Mini,
                    ImageName = "search_small",
                    ColorNormal = Color.FromHex("#3F51B5"),
                    ColorPressed = Color.FromHex("#7885cb"),
                    ColorRipple = Color.FromHex("#2C3E50"),
                    Clicked = (sender, ea) => SearchForPlacesNearby()
                });

                RefineButton.Children.Add(new FloatingActionButtonView
                {
                    Size = FloatingActionButtonSize.Mini,
                    ImageName = "ic_more_vert_white",
                    ColorNormal = Color.FromHex("#3F51B5"),
                    ColorPressed = Color.FromHex("#7885cb"),
                    ColorRipple = Color.FromHex("#2C3E50"),
                    Clicked = (sender, e) => ToggleRefineOptions()
                });
            }
            else
            {
                Button btnSearch = new Button
                {
                    BorderRadius = 20,
                    BackgroundColor = Color.FromHex("#3F51B5"),
                    //BorderColor = Color.FromHex("#3F51B5"),
                    //BorderWidth = 2,
                    WidthRequest = 100,
                    TextColor = Color.White,
                    Text = "Search",
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 40
                };

                btnSearch.Clicked += (sender, ea) => SearchForPlacesNearby();

                Button btnRefineSearch = new Button
                {
                    BorderRadius = 20,
                    BackgroundColor = Color.FromHex("#3F51B5"),
                    //BorderColor = Color.FromHex("#3F51B5"),
                    //BorderWidth = 2,
                    WidthRequest = 100,
                    TextColor = Color.White,
                    Text = "Filter",
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 40
                };

                btnRefineSearch.Clicked += (sender, e) => ToggleRefineOptions();

                SearchButton.Children.Add(btnSearch);
                RefineButton.Children.Add(btnRefineSearch);
            }
        }

        public class DistanceCalculation
        {
            public static class GeoCodeCalc
            {
                public const double EarthRadiusInMiles = 3956.0;
                public const double EarthRadiusInKilometers = 6367.0;

                public static double ToRadian(double val) { return val * (Math.PI / 180); }
                public static double DiffRadian(double val1, double val2) { return ToRadian(val2) - ToRadian(val1); }

                public static double CalcDistance(double lat1, double lng1, double lat2, double lng2)
                {
                    return CalcDistance(lat1, lng1, lat2, lng2, GeoCodeCalcMeasurement.Miles);
                }

                public static double CalcDistance(double lat1, double lng1, double lat2, double lng2, GeoCodeCalcMeasurement m)
                {
                    double radius = GeoCodeCalc.EarthRadiusInMiles;

                    if (m == GeoCodeCalcMeasurement.Kilometers) { radius = GeoCodeCalc.EarthRadiusInKilometers; }
                    return radius * 2 * Math.Asin(Math.Min(1, Math.Sqrt((Math.Pow(Math.Sin((DiffRadian(lat1, lat2)) / 2.0), 2.0) + Math.Cos(ToRadian(lat1)) * Math.Cos(ToRadian(lat2)) * Math.Pow(Math.Sin((DiffRadian(lng1, lng2)) / 2.0), 2.0)))));
                }
            }

            public enum GeoCodeCalcMeasurement : int
            {
                Miles = 0,
                Kilometers = 1
            }
        }
        
        public class Locations
        {
            public double longitude { get; set; }
            public double latitude { get; set; }
            public string LocationName { get; set; }
            public string Description { get; set; }
        }
    }
}
