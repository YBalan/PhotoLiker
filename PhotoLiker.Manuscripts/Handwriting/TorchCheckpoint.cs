using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PhotoLiker.Manuscripts.Handwriting
{
    /// <summary>One tensor of a PyTorch checkpoint: its shape, and its numbers (any float or integer type, as floats) row after row.</summary>
    public sealed class TorchTensor(int[] shape, float[] values)
    {
        public int[] Shape { get; } = shape;

        public float[] Values { get; } = values;
    }

    /// <summary>
    /// What a PyTorch checkpoint (a <c>.pt</c> file, written by <c>torch.save</c>) holds that a model needs: the weights by name, and the characters the model reads when the file says them.
    /// The file is a zip of a pickle (the dictionary, with the tensors named in it) and of the numbers of each tensor. It is read here by a small interpreter of the pickle that only builds the
    /// dictionary, lists and tensors, and never runs anything the file names (a pickle can ask for any function of any library, so a pickle that is loaded the usual way is a program): a thing
    /// the interpreter does not know becomes an inert placeholder that is left alone.
    /// </summary>
    public sealed class TorchCheckpoint
    {
        private TorchCheckpoint(IReadOnlyDictionary<string, TorchTensor> weights, IReadOnlyDictionary<int, string>? characters)
        {
            Weights = weights;
            Characters = characters;
        }

        /// <summary>The tensors of the model by their names (<c>cnn.0.weight</c>, <c>rnn.weight_ih_l0</c>...), without the <c>module.</c> that a model trained on several cards puts before each.</summary>
        public IReadOnlyDictionary<string, TorchTensor> Weights { get; }

        /// <summary>The character for each answer the model gives, when the checkpoint holds that (the answer 0 is the blank of the CTC and is not in it); null when it does not.</summary>
        public IReadOnlyDictionary<int, string>? Characters { get; }

        /// <exception cref="InvalidDataException">The file is not a PyTorch checkpoint, or is one that this reader cannot read.</exception>
        public static TorchCheckpoint Read(string path)
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }

        /// <exception cref="InvalidDataException">The stream is not a PyTorch checkpoint, or is one that this reader cannot read.</exception>
        public static TorchCheckpoint Read(Stream stream)
        {
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("The file is not a PyTorch checkpoint (it is not a zip file; checkpoints of PyTorch before 1.6 are not read).", ex);
            }

            using (archive)
            {
                var pickleEntry = archive.Entries.FirstOrDefault(e => e.FullName == "data.pkl" || e.FullName.EndsWith("/data.pkl", StringComparison.Ordinal))
                    ?? throw new InvalidDataException("The file is not a PyTorch checkpoint: it has no data.pkl.");
                var root = pickleEntry.FullName[..^"data.pkl".Length];

                byte[] pickle;
                using (var input = pickleEntry.Open())
                using (var copy = new MemoryStream())
                {
                    input.CopyTo(copy);
                    pickle = copy.ToArray();
                }

                var storages = new Dictionary<string, byte[]>();
                byte[] StorageBytes(string key)
                {
                    if (storages.TryGetValue(key, out var known))
                        return known;

                    var entry = archive.GetEntry(root + "data/" + key) ?? throw new InvalidDataException($"The checkpoint has no numbers for its storage {key}.");
                    using var input = entry.Open();
                    using var copy = new MemoryStream();
                    input.CopyTo(copy);
                    return storages[key] = copy.ToArray();
                }

                object? top;
                try
                {
                    top = new PickleReader(pickle).Read();
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidCastException or KeyNotFoundException or InvalidOperationException)
                {
                    throw new InvalidDataException("The checkpoint's pickle is damaged or of a kind this reader does not know: " + ex.Message, ex);
                }

                if (top is not Dictionary<object, object?> checkpoint)
                    throw new InvalidDataException("The checkpoint does not hold a dictionary.");

                var state = checkpoint.TryGetValue("model_state_dict", out var named) ? named
                    : checkpoint.TryGetValue("state_dict", out named) ? named
                    : checkpoint;
                if (state is not Dictionary<object, object?> weights)
                    throw new InvalidDataException("The checkpoint's model_state_dict is not a dictionary.");

                var tensors = new Dictionary<string, TorchTensor>(StringComparer.Ordinal);
                foreach (var (key, value) in weights)
                {
                    if (key is not string name || value is not PickleTensor tensor)
                        continue;

                    if (name.StartsWith("module.", StringComparison.Ordinal))
                        name = name["module.".Length..];
                    if (tensor.Storage.Kind is null)
                        continue; // a kind of number this reader does not know (the counters of a normalization are not needed)

                    tensors[name] = tensor.ToTensor(StorageBytes(tensor.Storage.Key));
                }

                return new TorchCheckpoint(tensors, ReadCharacters(checkpoint));
            }
        }

        private static Dictionary<int, string>? ReadCharacters(Dictionary<object, object?> checkpoint)
        {
            if (!checkpoint.TryGetValue("idx2char", out var value) || value is not Dictionary<object, object?> map)
                return null;

            var characters = new Dictionary<int, string>();
            foreach (var (key, text) in map)
            {
                if (key is long index && text is string character)
                    characters[(int)index] = character;
            }

            return characters.Count == 0 ? null : characters;
        }

        // ── What the pickle is made of ─────────────────────────────────────

        /// <summary>The storage of a tensor: the numbers of the file <c>data/&lt;key&gt;</c> of the zip. The kind is null for a kind of number that is not read.</summary>
        private sealed record PickleStorage(string Key, string? Kind);

        private sealed class PickleTensor(PickleStorage storage, long offset, int[] shape, long[] stride)
        {
            public PickleStorage Storage { get; } = storage;

            public TorchTensor ToTensor(byte[] bytes)
            {
                var size = Storage.Kind switch { "float32" or "int32" => 4, "float64" or "int64" => 8, "float16" => 2, "int16" => 2, "uint8" or "int8" or "bool" => 1, _ => throw new InvalidDataException($"A tensor of kind {Storage.Kind} is not read.") };
                var count = 1;
                foreach (var extent in shape)
                    count = checked(count * extent);

                var values = new float[count];
                var index = new int[shape.Length];
                for (var n = 0; n < count; n++)
                {
                    long at = offset;
                    for (var d = 0; d < shape.Length; d++)
                        at += index[d] * stride[d];

                    var position = checked((int)(at * size));
                    if (position < 0 || position + size > bytes.Length)
                        throw new InvalidDataException("A tensor of the checkpoint reaches past the numbers of its storage.");

                    var span = bytes.AsSpan(position, size);
                    values[n] = Storage.Kind switch
                    {
                        "float32" => BinaryPrimitives.ReadSingleLittleEndian(span),
                        "float64" => (float)BinaryPrimitives.ReadDoubleLittleEndian(span),
                        "float16" => (float)BinaryPrimitives.ReadHalfLittleEndian(span),
                        "int64" => BinaryPrimitives.ReadInt64LittleEndian(span),
                        "int32" => BinaryPrimitives.ReadInt32LittleEndian(span),
                        "int16" => BinaryPrimitives.ReadInt16LittleEndian(span),
                        "int8" => (sbyte)span[0],
                        _ => span[0],
                    };

                    for (var d = shape.Length - 1; d >= 0; d--)
                    {
                        if (++index[d] < shape[d])
                            break;
                        index[d] = 0;
                    }
                }

                return new TorchTensor(shape, values);
            }
        }

        /// <summary>A thing (a function, a class) the pickle names by its module and its name; it is never looked up or run.</summary>
        private sealed record PickleGlobal(string Module, string Name);

        /// <summary>A thing the pickle makes that this reader does not know: kept so that the pickle goes on, and not used for anything.</summary>
        private sealed record PickleThing(PickleGlobal Type);

        private sealed class PickleMark;

        /// <summary>The pickle machine: the stack of values the opcodes of the pickle work on, the memo they put values in and get them from again.</summary>
        private sealed class PickleReader(byte[] data)
        {
            private readonly List<object?> _stack = [];
            private readonly Dictionary<long, object?> _memo = [];
            private int _position;

            private static readonly PickleMark Mark = new();

            public object? Read()
            {
                while (true)
                {
                    var op = data[_position++];
                    switch (op)
                    {
                        case 0x80: _position++; break; // PROTO
                        case 0x95: _position += 8; break; // FRAME
                        case (byte)'.': return _stack[^1];
                        case (byte)'(': _stack.Add(Mark); break;
                        case (byte)'0': Pop(); break;
                        case (byte)'1': PopToMark(); break;
                        case (byte)'2': _stack.Add(_stack[^1]); break;
                        case (byte)'N': _stack.Add(null); break;
                        case 0x88: _stack.Add(true); break;
                        case 0x89: _stack.Add(false); break;
                        case (byte)'}': _stack.Add(new Dictionary<object, object?>()); break;
                        case (byte)']': _stack.Add(new List<object?>()); break;
                        case (byte)')': _stack.Add(Array.Empty<object?>()); break;
                        case (byte)'J': _stack.Add((long)BinaryPrimitives.ReadInt32LittleEndian(Take(4))); break;
                        case (byte)'K': _stack.Add((long)Take(1)[0]); break;
                        case (byte)'M': _stack.Add((long)BinaryPrimitives.ReadUInt16LittleEndian(Take(2))); break;
                        case 0x8a: _stack.Add(LongNumber(Take(Take(1)[0]))); break; // LONG1
                        case 0x8b: _stack.Add(LongNumber(Take(BinaryPrimitives.ReadInt32LittleEndian(Take(4))))); break; // LONG4
                        case (byte)'G': _stack.Add(BinaryPrimitives.ReadDoubleBigEndian(Take(8))); break;
                        case (byte)'X': _stack.Add(Encoding.UTF8.GetString(Take(BinaryPrimitives.ReadInt32LittleEndian(Take(4))))); break; // BINUNICODE
                        case 0x8c: _stack.Add(Encoding.UTF8.GetString(Take(Take(1)[0]))); break; // SHORT_BINUNICODE
                        case (byte)'U': _stack.Add(Encoding.Latin1.GetString(Take(Take(1)[0]))); break; // SHORT_BINSTRING
                        case (byte)'T': _stack.Add(Encoding.Latin1.GetString(Take(BinaryPrimitives.ReadInt32LittleEndian(Take(4))))); break; // BINSTRING
                        case (byte)'C': _stack.Add(Take(Take(1)[0]).ToArray()); break; // SHORT_BINBYTES
                        case (byte)'B': _stack.Add(Take(BinaryPrimitives.ReadInt32LittleEndian(Take(4))).ToArray()); break; // BINBYTES
                        case (byte)'c': _stack.Add(new PickleGlobal(Line(), Line())); break; // GLOBAL
                        case 0x93: // STACK_GLOBAL
                            {
                                var name = (string)Pop()!;
                                var module = (string)Pop()!;
                                _stack.Add(new PickleGlobal(module, name));
                                break;
                            }
                        case 0x94: _memo[_memo.Count] = _stack[^1]; break; // MEMOIZE
                        case (byte)'q': _memo[Take(1)[0]] = _stack[^1]; break; // BINPUT
                        case (byte)'r': _memo[BinaryPrimitives.ReadUInt32LittleEndian(Take(4))] = _stack[^1]; break; // LONG_BINPUT
                        case (byte)'h': _stack.Add(_memo[Take(1)[0]]); break; // BINGET
                        case (byte)'j': _stack.Add(_memo[BinaryPrimitives.ReadUInt32LittleEndian(Take(4))]); break; // LONG_BINGET
                        case (byte)'t': _stack.Add(PopToMark().ToArray()); break; // TUPLE
                        case 0x85: _stack.Add(PopTuple(1)); break;
                        case 0x86: _stack.Add(PopTuple(2)); break;
                        case 0x87: _stack.Add(PopTuple(3)); break;
                        case (byte)'l': _stack.Add(PopToMark()); break; // LIST
                        case (byte)'d': // DICT
                            {
                                var items = PopToMark();
                                var dictionary = new Dictionary<object, object?>();
                                for (var i = 0; i + 1 < items.Count; i += 2)
                                    dictionary[items[i]!] = items[i + 1];
                                _stack.Add(dictionary);
                                break;
                            }
                        case (byte)'s': // SETITEM
                            {
                                var value = Pop();
                                var key = Pop();
                                SetItem(_stack[^1], key, value);
                                break;
                            }
                        case (byte)'u': // SETITEMS
                            {
                                var items = PopToMark();
                                for (var i = 0; i + 1 < items.Count; i += 2)
                                    SetItem(_stack[^1], items[i], items[i + 1]);
                                break;
                            }
                        case (byte)'a': // APPEND
                            {
                                var value = Pop();
                                if (_stack[^1] is List<object?> list)
                                    list.Add(value);
                                break;
                            }
                        case (byte)'e': // APPENDS
                            {
                                var items = PopToMark();
                                if (_stack[^1] is List<object?> list)
                                    list.AddRange(items);
                                break;
                            }
                        case (byte)'R': // REDUCE: the call of a function with its arguments
                            {
                                var arguments = (object?[])Pop()!;
                                var function = Pop();
                                _stack.Add(Call(function, arguments));
                                break;
                            }
                        case 0x81: // NEWOBJ: a class and the arguments of its making
                            {
                                Pop();
                                var type = Pop();
                                _stack.Add(new PickleThing(type as PickleGlobal ?? new PickleGlobal("?", "?")));
                                break;
                            }
                        case (byte)'b': Pop(); break; // BUILD: the state of an object, which nothing here needs
                        case (byte)'Q': // BINPERSID: a storage of the zip, named by the tuple on the stack
                            {
                                _stack.Add(Storage((object?[])Pop()!));
                                break;
                            }
                        default:
                            throw new InvalidDataException($"The checkpoint's pickle uses the opcode 0x{op:x2} at {_position - 1}, which this reader does not know.");
                    }
                }
            }

            private ReadOnlySpan<byte> Take(int count)
            {
                var span = new ReadOnlySpan<byte>(data, _position, count);
                _position += count;
                return span;
            }

            private string Line()
            {
                var start = _position;
                while (data[_position] != (byte)'\n')
                    _position++;
                var text = Encoding.UTF8.GetString(data, start, _position - start);
                _position++;
                return text;
            }

            private object? Pop()
            {
                var value = _stack[^1];
                _stack.RemoveAt(_stack.Count - 1);
                return value;
            }

            private List<object?> PopToMark()
            {
                var at = _stack.FindLastIndex(item => ReferenceEquals(item, Mark));
                if (at < 0)
                    throw new InvalidDataException("The checkpoint's pickle ends a group that it did not begin.");

                var items = _stack.GetRange(at + 1, _stack.Count - at - 1);
                _stack.RemoveRange(at, _stack.Count - at);
                return items;
            }

            private object?[] PopTuple(int count)
            {
                var items = _stack.GetRange(_stack.Count - count, count).ToArray();
                _stack.RemoveRange(_stack.Count - count, count);
                return items;
            }

            private static void SetItem(object? target, object? key, object? value)
            {
                if (target is Dictionary<object, object?> dictionary && key is not null)
                    dictionary[key] = value;
            }

            private static long LongNumber(ReadOnlySpan<byte> bytes)
            {
                if (bytes.Length == 0)
                    return 0;

                long value = (bytes[^1] & 0x80) != 0 ? -1 : 0;
                for (var i = Math.Min(bytes.Length, 8) - 1; i >= 0; i--)
                    value = (value << 8) | bytes[i];
                return value;
            }

            /// <summary>The call of a function the pickle names: the few it needs for tensors are done, any other makes a placeholder.</summary>
            private static object? Call(object? function, object?[] arguments)
            {
                if (function is not PickleGlobal global)
                    return new PickleThing(new PickleGlobal("?", "?"));

                switch ((global.Module, global.Name))
                {
                    case ("collections", "OrderedDict"):
                        return new Dictionary<object, object?>();
                    case ("torch._utils", "_rebuild_tensor_v2" or "_rebuild_tensor"):
                        return new PickleTensor((PickleStorage)arguments[0]!, (long)arguments[1]!, Ints(arguments[2]), Longs(arguments[3]));
                    case ("torch._utils", "_rebuild_parameter" or "_rebuild_parameter_with_state"):
                        return arguments[0];
                    default:
                        return new PickleThing(global);
                }
            }

            private static int[] Ints(object? tuple) => ((object?[])tuple!).Select(item => checked((int)(long)item!)).ToArray();

            private static long[] Longs(object? tuple) => ((object?[])tuple!).Select(item => (long)item!).ToArray();

            /// <summary>The persistent id of a storage: <c>('storage', torch.FloatStorage, '0', 'cpu', 1234)</c>.</summary>
            private static PickleStorage Storage(object?[] id)
            {
                if (id.Length < 3 || id[0] as string != "storage")
                    throw new InvalidDataException("The checkpoint's pickle refers to something that is not a storage.");

                var kind = (id[1] as PickleGlobal)?.Name switch
                {
                    "FloatStorage" => "float32",
                    "DoubleStorage" => "float64",
                    "HalfStorage" => "float16",
                    "LongStorage" => "int64",
                    "IntStorage" => "int32",
                    "ShortStorage" => "int16",
                    "CharStorage" => "int8",
                    "ByteStorage" => "uint8",
                    "BoolStorage" => "bool",
                    _ => null,
                };
                return new PickleStorage(Convert.ToString(id[2], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, kind);
            }
        }
    }
}
