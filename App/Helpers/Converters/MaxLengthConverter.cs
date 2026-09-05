
using System.Globalization;

namespace GamHubApp.Helpers;

public class MaxLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is Binding binding)
        {
            var src = binding.Source;
            var propName = binding.Path;

            if (propName.Contains('.'))
            {
                var lastName = propName.Split('.').Last();
                propName = src?.GetType().GetProperty(lastName)?.GetValue(src, null).ToString();
                // NOTE: for any maintenance read https://benetskyybogdan.medium.com/converter-parameter-binding-how-to-bind-complex-values-at-xamarin-maui-a23b6c45ab31

            }
            parameter = src?.GetType().GetProperty(propName)?.GetValue(src, null).ToString();
        }
        int maxLength = int.Parse((string)parameter);
        string text = (string)value;
        if (string.IsNullOrEmpty(text))
            return 0;
        if (text.Length > maxLength)
        {
            return text.Substring(0, maxLength) + "...";
        }
        return text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
