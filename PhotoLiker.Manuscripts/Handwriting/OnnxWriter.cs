using System.Buffers.Binary;
using System.Text;

namespace PhotoLiker.Manuscripts.Handwriting
{
    /// <summary>One attribute of a node of an ONNX graph.</summary>
    internal sealed record OnnxAttribute(string Name, long? Int = null, float? Float = null, string? Text = null, long[]? Ints = null)
    {
        public static OnnxAttribute Of(string name, long value) => new(name, Int: value);

        public static OnnxAttribute Of(string name, float value) => new(name, Float: value);

        public static OnnxAttribute Of(string name, string value) => new(name, Text: value);

        public static OnnxAttribute Of(string name, params long[] values) => new(name, Ints: values);
    }

    /// <summary>
    /// Writes a small neural network as an ONNX file (the protocol buffer of ModelProto, field by field: there is no library for that in this program, and the networks it writes are a dozen nodes).
    /// Only what a network of <see cref="CrnnConverter"/> needs: float tensors with named or fixed sizes, int64 constants, nodes with int, float, string and int list attributes.
    /// </summary>
    internal sealed class OnnxWriter
    {
        private const int FloatType = 1;
        private const int Int64Type = 7;

        private readonly List<byte[]> _nodes = [];
        private readonly List<byte[]> _initializers = [];
        private readonly List<(string Key, string Value)> _metadata = [];
        private readonly string _name;
        private readonly (string Name, object[] Dimensions) _input;
        private readonly (string Name, object[] Dimensions) _output;

        /// <param name="name">The name of the graph.</param>
        /// <param name="input">The name of the one input, and its sizes: a number is a fixed size, a string a size that is found when the network runs.</param>
        /// <param name="output">The name of the one output, and its sizes.</param>
        public OnnxWriter(string name, (string Name, object[] Dimensions) input, (string Name, object[] Dimensions) output)
        {
            _name = name;
            _input = input;
            _output = output;
        }

        /// <summary>A tensor of float numbers that the network has with it (weights).</summary>
        public void Floats(string name, float[] values, params long[] dimensions)
        {
            var expected = dimensions.Aggregate(1L, (all, d) => all * d);
            if (expected != values.Length)
                throw new ArgumentException($"A tensor of {string.Join(" by ", dimensions)} has {expected} numbers, not {values.Length}.", nameof(values));

            var raw = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(i * 4), values[i]);
            _initializers.Add(Tensor(name, FloatType, dimensions, raw));
        }

        /// <summary>A list of whole numbers that the network has with it (a shape).</summary>
        public void Integers(string name, params long[] values)
        {
            var raw = new byte[values.Length * 8];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(i * 8), values[i]);
            _initializers.Add(Tensor(name, Int64Type, [values.Length], raw));
        }

        /// <summary>A note that goes with the network in the file (ONNX Runtime hands it back as the custom metadata of the model): the characters a handwriting network reads travel with it.</summary>
        public void Metadata(string key, string value) => _metadata.Add((key, value));

        /// <summary>A step of the network: the operator, what goes in, what comes out (an empty name is an output that is not wanted).</summary>
        public void Node(string op, string[] inputs, string[] outputs, params OnnxAttribute[] attributes)
        {
            var node = new Proto();
            foreach (var input in inputs)
                node.Text(1, input);
            foreach (var output in outputs)
                node.Text(2, output);
            node.Text(3, outputs.Length > 0 && outputs[0].Length > 0 ? outputs[0] : op);
            node.Text(4, op);
            foreach (var attribute in attributes)
                node.Message(5, Attribute(attribute));
            _nodes.Add(node.ToArray());
        }

        /// <summary>The file: the network, for the operators of version 17 of ONNX.</summary>
        public byte[] ToArray()
        {
            var graph = new Proto();
            foreach (var node in _nodes)
                graph.Message(1, node);
            graph.Text(2, _name);
            foreach (var initializer in _initializers)
                graph.Message(5, initializer);
            graph.Message(11, ValueInfo(_input.Name, _input.Dimensions));
            graph.Message(12, ValueInfo(_output.Name, _output.Dimensions));

            var opset = new Proto();
            opset.Text(1, string.Empty);
            opset.Varint(2, 17);

            var model = new Proto();
            model.Varint(1, 8); // the version of the file format
            model.Text(2, "PhotoLiker");
            model.Message(7, graph.ToArray());
            model.Message(8, opset.ToArray());
            foreach (var (key, value) in _metadata)
            {
                var entry = new Proto();
                entry.Text(1, key);
                entry.Text(2, value);
                model.Message(14, entry.ToArray());
            }

            return model.ToArray();
        }

        private static byte[] Tensor(string name, int type, long[] dimensions, byte[] raw)
        {
            var tensor = new Proto();
            foreach (var dimension in dimensions)
                tensor.Varint(1, dimension);
            tensor.Varint(2, type);
            tensor.Text(8, name);
            tensor.Bytes(9, raw);
            return tensor.ToArray();
        }

        private static byte[] Attribute(OnnxAttribute attribute)
        {
            var message = new Proto();
            message.Text(1, attribute.Name);
            if (attribute.Int is { } integer)
            {
                message.Varint(3, integer);
                message.Varint(20, 2);
            }
            else if (attribute.Float is { } number)
            {
                message.Float(2, number);
                message.Varint(20, 1);
            }
            else if (attribute.Text is { } text)
            {
                message.Bytes(4, Encoding.UTF8.GetBytes(text));
                message.Varint(20, 3);
            }
            else
            {
                foreach (var value in attribute.Ints ?? [])
                    message.Varint(8, value);
                message.Varint(20, 7);
            }

            return message.ToArray();
        }

        private static byte[] ValueInfo(string name, object[] dimensions)
        {
            var shape = new Proto();
            foreach (var dimension in dimensions)
            {
                var one = new Proto();
                if (dimension is string symbol)
                    one.Text(2, symbol);
                else
                    one.Varint(1, Convert.ToInt64(dimension, System.Globalization.CultureInfo.InvariantCulture));
                shape.Message(1, one.ToArray());
            }

            var tensor = new Proto();
            tensor.Varint(1, FloatType);
            tensor.Message(2, shape.ToArray());

            var type = new Proto();
            type.Message(1, tensor.ToArray());

            var info = new Proto();
            info.Text(1, name);
            info.Message(2, type.ToArray());
            return info.ToArray();
        }

        /// <summary>A message of the protocol buffers wire format: fields, each a number and a kind of value.</summary>
        private sealed class Proto
        {
            private readonly MemoryStream _bytes = new();

            public byte[] ToArray() => _bytes.ToArray();

            public void Varint(int field, long value)
            {
                Key(field, 0);
                WriteVarint((ulong)value);
            }

            public void Float(int field, float value)
            {
                Key(field, 5);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
                _bytes.Write(buffer);
            }

            public void Bytes(int field, byte[] value)
            {
                Key(field, 2);
                WriteVarint((ulong)value.Length);
                _bytes.Write(value);
            }

            public void Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

            public void Message(int field, byte[] value) => Bytes(field, value);

            private void Key(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

            private void WriteVarint(ulong value)
            {
                while (value >= 0x80)
                {
                    _bytes.WriteByte((byte)(value | 0x80));
                    value >>= 7;
                }

                _bytes.WriteByte((byte)value);
            }
        }
    }
}
