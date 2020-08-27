using System;
using System.Windows;

namespace CleanSlate
{
    // This is an extremely basic and rudimentary way to switch themes with MahApps.Metro
    // Their "ThemeManager" class is much more robust and flexible. However, it is very
    // code heavy and was too complex for the scope of this template application.
    class ThemeManagerLite
    {
        public ThemeManagerLite()
        {

        }

        public void ChangeTheme(string theme)
        {
            ResourceDictionary newTheme = new ResourceDictionary();
            ResourceDictionary priorTheme = new ResourceDictionary();

            // find the old theme
            foreach (ResourceDictionary dict in Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Source.ToString().Contains("Themes"))
                {
                    priorTheme = dict;
                }
            }

            // remove the theme
            Application.Current.Resources.MergedDictionaries.Remove(priorTheme);

            // replace the theme
            switch (theme)
            {
                case "Dark":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Blue.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Slate":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Steel.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Pro":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Steel.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Drab":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Olive.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Crimson":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Crimson.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Construction":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Yellow.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Bubblegum":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Pink.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Formula":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Red.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                case "Lavender":
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Purple.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
                default:
                    newTheme.Source = new Uri("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml");
                    Application.Current.Resources.MergedDictionaries.Add(newTheme);
                    break;
            }

            // save the new theme in app settings to persist
            Properties.Settings.Default.Theme = theme;
            Properties.Settings.Default.Save();
        }
    }
}
