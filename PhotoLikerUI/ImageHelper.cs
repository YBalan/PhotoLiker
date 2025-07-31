namespace PhotoLikerUI
{
    using System;
    using System.ComponentModel;
    using System.Drawing;
    using System.Drawing.Imaging;

    public static class ImageHelper
    {
        public static Image LoadImageWithCorrectOrientation(string path)
        {
            var img = Image.FromFile(path);

            const int ExifOrientationId = 0x0112; // 274

            if (img.PropertyIdList.Contains(ExifOrientationId))
            {
                var prop = img.GetPropertyItem(ExifOrientationId);
                int orientation = BitConverter.ToUInt16(prop.Value, 0);

                RotateFlipType rotateFlip = RotateFlipType.RotateNoneFlipNone;

                switch (orientation)
                {
                    case 2: rotateFlip = RotateFlipType.RotateNoneFlipX; break;
                    case 3: rotateFlip = RotateFlipType.Rotate180FlipNone; break;
                    case 4: rotateFlip = RotateFlipType.Rotate180FlipX; break;
                    case 5: rotateFlip = RotateFlipType.Rotate90FlipX; break;
                    case 6: rotateFlip = RotateFlipType.Rotate90FlipNone; break;
                    case 7: rotateFlip = RotateFlipType.Rotate270FlipX; break;
                    case 8: rotateFlip = RotateFlipType.Rotate270FlipNone; break;
                }

                if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
                {
                    img.RotateFlip(rotateFlip);
                    // Remove the orientation tag so it doesn't get re-applied
                    //img.RemovePropertyItem(ExifOrientationId);
                }
            }

            return img;
        }
    }

    public static class ExifTagMap
    {
        public static readonly Dictionary<int, string> Tags = new()
        {
            [0x010F] = "Make",
            [0x0110] = "Model",
            [0x0131] = "Software",
            [0x0132] = "Date Taken",
            [0x829A] = "Exposure Time",
            [0x829D] = "F-Number",
            [0x8827] = "ISO Speed",
            [0x9003] = "Date Taken (Original)",
            [0x9201] = "Shutter Speed",
            [0x9202] = "Aperture",
            [0x9209] = "Flash",
            [0xA002] = "Width",
            [0xA003] = "Height",
            [0x0112] = "Orientation"
        };
    }

    public class FriendlyImageMetadata
    {
        public Dictionary<string, string> Properties { get; } = [];

        public FriendlyImageMetadata(Image image)
        {
            foreach (var prop in image.PropertyItems)
            {
                string name = ExifTagMap.Tags.TryGetValue(prop.Id, out var friendly)
                    ? friendly
                    : $"Unknown (0x{prop.Id:X4})";

                string value = ParseExifValue(prop);
                if (!Properties.ContainsKey(name))
                    Properties[name] = value;
            }
        }

        private string ParseExifValue(PropertyItem item)
        {
            try
            {
                return item.Type switch
                {
                    2 => System.Text.Encoding.ASCII.GetString(item.Value).Trim('\0'), // ASCII
                    3 => BitConverter.ToUInt16(item.Value, 0).ToString(),             // Short
                    4 => BitConverter.ToUInt32(item.Value, 0).ToString(),             // Long
                    5 => GetRational(item),                                           // Rational
                    10 => GetSRational(item),                                         // SRational
                    _ => BitConverter.ToString(item.Value)
                };
            }
            catch
            {
                return BitConverter.ToString(item.Value);
            }
        }

        private string GetRational(PropertyItem item)
        {
            uint num = BitConverter.ToUInt32(item.Value, 0);
            uint den = BitConverter.ToUInt32(item.Value, 4);
            if (den == 0) return "∞";
            return $"{num}/{den} ({(double)num / den:0.###})";
        }

        private string GetSRational(PropertyItem item)
        {
            int num = BitConverter.ToInt32(item.Value, 0);
            int den = BitConverter.ToInt32(item.Value, 4);
            if (den == 0) return "∞";
            return $"{num}/{den} ({(double)num / den:0.###})";
        }
    }



    public class MetadataWrapper : ICustomTypeDescriptor
    {
        private readonly Dictionary<string, string> _data;

        public MetadataWrapper(Dictionary<string, string> data)
        {
            _data = data;
        }

        public PropertyDescriptorCollection GetProperties()
        {
            var props = _data.Select(kv => new MetadataPropertyDescriptor(kv.Key, kv.Value)).ToArray();
            return new PropertyDescriptorCollection(props);
        }

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();

        // Minimal ICustomTypeDescriptor implementation
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public string GetClassName() => null;
        public string GetComponentName() => null;
        public TypeConverter GetConverter() => null;
        public EventDescriptor GetDefaultEvent() => null;
        public PropertyDescriptor GetDefaultProperty() => null;
        public object GetEditor(Type editorBaseType) => null;
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public object GetPropertyOwner(PropertyDescriptor pd) => this;
    }

    public class MetadataPropertyDescriptor : PropertyDescriptor
    {
        private readonly string _value;

        public MetadataPropertyDescriptor(string name, string value)
            : base(name, null)
        {
            _value = value;
        }

        public override Type PropertyType => typeof(string);
        public override void SetValue(object component, object value) { }
        public override object GetValue(object component) => _value;
        public override bool IsReadOnly => true;
        public override Type ComponentType => typeof(object);
        public override bool CanResetValue(object component) => false;
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }

}
