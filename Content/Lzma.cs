namespace NullServers.Content
{
    internal sealed class Lzma
    {
        private const int States = 12;
        private const int EndPosModel = 14;
        private const int FullDistances = 128;

        private readonly byte[] _source;
        private readonly byte[] _output;
        private int _input;
        private uint _range = 0xFFFFFFFF;
        private uint _code;
        private int _written;

        private readonly ushort[] _literals;
        private readonly ushort[] _isMatch = Probabilities(States << 4);
        private readonly ushort[] _isRep = Probabilities(States);
        private readonly ushort[] _isRepG0 = Probabilities(States);
        private readonly ushort[] _isRepG1 = Probabilities(States);
        private readonly ushort[] _isRepG2 = Probabilities(States);
        private readonly ushort[] _isRep0Long = Probabilities(States << 4);
        private readonly ushort[] _slots = Probabilities(4 << 6);
        private readonly ushort[] _distances = Probabilities(1 + FullDistances - EndPosModel);
        private readonly ushort[] _align = Probabilities(1 << 4);
        private readonly Lengths _lengths = new();
        private readonly Lengths _repeats = new();

        private Lzma(byte[] source, int size, int literalStates)
        {
            _source = source;
            _output = new byte[size];
            _literals = Probabilities(0x300 << literalStates);
        }

        internal static byte[] Decode(byte[] source, int size)
        {
            if (source.Length < 6)
            {
                throw new InvalidDataException("Corrupt LZMA header.");
            }

            int properties = source[0];
            if (properties >= 9 * 5 * 5)
            {
                throw new InvalidDataException("Corrupt LZMA properties.");
            }

            int lc = properties % 9;
            int lp = properties / 9 % 5;
            int pb = properties / 45;
            Lzma decoder = new(source, size, lc + lp) { _input = 5 };
            decoder.Start();
            decoder.Run(lc, lp, pb);
            return decoder._output;
        }

        private static ushort[] Probabilities(int count)
        {
            ushort[] values = new ushort[count];
            Array.Fill(values, (ushort)1024);
            return values;
        }

        private byte Next() => _input < _source.Length ? _source[_input++] : throw new InvalidDataException("Truncated LZMA data.");

        private void Start()
        {
            byte first = Next();
            for (int index = 0; index < 4; index++)
            {
                _code = _code << 8 | Next();
            }

            if (first != 0 || _code == _range)
            {
                throw new InvalidDataException("Corrupt LZMA stream.");
            }
        }

        private void Normalize()
        {
            if (_range < 1u << 24)
            {
                _range <<= 8;
                _code = _code << 8 | Next();
            }
        }

        private int Bit(ushort[] probabilities, int index)
        {
            uint probability = probabilities[index];
            uint bound = (_range >> 11) * probability;
            int bit;

            if (_code < bound)
            {
                _range = bound;
                probabilities[index] = (ushort)(probability + ((2048 - probability) >> 5));
                bit = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probabilities[index] = (ushort)(probability - (probability >> 5));
                bit = 1;
            }

            Normalize();
            return bit;
        }

        private uint Direct(int count)
        {
            uint result = 0;
            for (; count > 0; count--)
            {
                _range >>= 1;
                _code -= _range;
                uint mask = 0 - (_code >> 31);
                _code += _range & mask;

                if (_code == _range)
                {
                    throw new InvalidDataException("Corrupt LZMA stream.");
                }

                Normalize();
                result = (result << 1) + mask + 1;
            }

            return result;
        }

        private int Tree(ushort[] probabilities, int offset, int bits)
        {
            int node = 1;
            for (int index = 0; index < bits; index++)
            {
                node = (node << 1) + Bit(probabilities, offset + node);
            }

            return node - (1 << bits);
        }

        private int ReverseTree(ushort[] probabilities, int offset, int bits)
        {
            int node = 1;
            int result = 0;

            for (int index = 0; index < bits; index++)
            {
                int bit = Bit(probabilities, offset + node);
                node = (node << 1) + bit;
                result |= bit << index;
            }

            return result;
        }

        private void Run(int lc, int lp, int pb)
        {
            int state = 0;
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            int positionMask = (1 << pb) - 1;
            int literalMask = (1 << lp) - 1;

            while (_written < _output.Length)
            {
                int position = _written & positionMask;

                if (Bit(_isMatch, (state << 4) + position) == 0)
                {
                    Literal(state, rep0, ((_written & literalMask) << lc) + (_written > 0 ? _output[_written - 1] >> (8 - lc) : 0));
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }

                int length;
                if (Bit(_isRep, state) != 0)
                {
                    if (_written == 0)
                    {
                        throw new InvalidDataException("Corrupt LZMA stream.");
                    }

                    if (Bit(_isRepG0, state) == 0)
                    {
                        if (Bit(_isRep0Long, (state << 4) + position) == 0)
                        {
                            state = state < 7 ? 9 : 11;
                            _output[_written] = _output[_written - rep0 - 1];
                            _written++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (Bit(_isRepG1, state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (Bit(_isRepG2, state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }

                            rep2 = rep1;
                        }

                        rep1 = rep0;
                        rep0 = distance;
                    }

                    length = _repeats.Decode(this, position);
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    length = _lengths.Decode(this, position);
                    state = state < 7 ? 7 : 10;
                    rep0 = Distance(length);

                    if (rep0 == 0xFFFFFFFF)
                    {
                        throw new InvalidDataException("The LZMA stream ended early.");
                    }

                    if (rep0 >= _written)
                    {
                        throw new InvalidDataException("Corrupt LZMA distance.");
                    }
                }

                length += 2;
                if (length > _output.Length - _written)
                {
                    throw new InvalidDataException("LZMA data is longer than its block.");
                }

                for (int index = 0; index < length; index++)
                {
                    _output[_written] = _output[_written - rep0 - 1];
                    _written++;
                }
            }
        }

        private void Literal(int state, uint rep0, int context)
        {
            int offset = 0x300 * context;
            int symbol = 1;

            if (state >= 7)
            {
                int match = _output[_written - rep0 - 1];
                while (symbol < 0x100)
                {
                    int matchBit = match >> 7 & 1;
                    match <<= 1;
                    int bit = Bit(_literals, offset + ((1 + matchBit) << 8) + symbol);
                    symbol = symbol << 1 | bit;

                    if (matchBit != bit) break;
                }
            }

            while (symbol < 0x100)
            {
                symbol = symbol << 1 | Bit(_literals, offset + symbol);
            }

            _output[_written++] = (byte)symbol;
        }

        private uint Distance(int length)
        {
            int slot = Tree(_slots, Math.Min(length, 3) << 6, 6);
            if (slot < 4) return (uint)slot;

            int bits = (slot >> 1) - 1;
            uint distance = (uint)(2 | slot & 1) << bits;

            if (slot < EndPosModel)
            {
                return distance + (uint)ReverseTree(_distances, (int)distance - slot, bits);
            }

            distance += Direct(bits - 4) << 4;
            return distance + (uint)ReverseTree(_align, 0, 4);
        }

        private sealed class Lengths
        {
            private readonly ushort[] _choice = Probabilities(2);
            private readonly ushort[] _low = Probabilities(16 << 3);
            private readonly ushort[] _middle = Probabilities(16 << 3);
            private readonly ushort[] _high = Probabilities(256);

            internal int Decode(Lzma decoder, int position)
            {
                if (decoder.Bit(_choice, 0) == 0) return decoder.Tree(_low, position << 3, 3);
                if (decoder.Bit(_choice, 1) == 0) return 8 + decoder.Tree(_middle, position << 3, 3);

                return 16 + decoder.Tree(_high, 0, 8);
            }
        }
    }
}
