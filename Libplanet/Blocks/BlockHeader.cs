#nullable enable
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Bencodex;
using Bencodex.Types;
using Libplanet.Store.Trie;

namespace Libplanet.Blocks
{
    /// <summary>
    /// Block header containing information about <see cref="Block{T}"/>s except transactions.
    /// </summary>
    public readonly struct BlockHeader : IPreEvaluationBlockHeader, IBlockExcerpt
    {
        internal static readonly byte[] ProtocolVersionKey = { 0x00 };

        internal static readonly byte[] IndexKey = { 0x69 }; // 'i'

        internal static readonly byte[] TimestampKey = { 0x74 }; // 't'

        internal static readonly byte[] DifficultyKey = { 0x64 }; // 'd'

        internal static readonly byte[] TotalDifficultyKey = { 0x54 }; // 'T'

        internal static readonly byte[] NonceKey = { 0x6e }; // 'n'

        internal static readonly byte[] MinerKey = { 0x6d }; // 'm'

        internal static readonly byte[] PreviousHashKey = { 0x70 }; // 'p'

        internal static readonly byte[] TxHashKey = { 0x78 }; // 'x'

        internal static readonly byte[] HashKey = { 0x68 }; // 'h'

        internal static readonly byte[] StateRootHashKey = { 0x73 }; // 's'

        internal static readonly byte[] PreEvaluationHashKey = { 0x63 }; // 'c'

        private const int CurrentProtocolVersion = BlockMetadata.CurrentProtocolVersion;
        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

        private static readonly Codec Codec = new Codec();

        /// <summary>
        /// Creates a <see cref="BlockHeader"/> instance by manually filling in all the properties.
        /// </summary>
        /// <param name="protocolVersion">The protocol version.  Goes to
        /// <see cref="ProtocolVersion"/>.</param>
        /// <param name="index">The height of the block.  Goes to <see cref="Index"/>.
        /// </param>
        /// <param name="timestamp">The time the block is created.
        /// Goes to <see cref="Timestamp"/>.</param>
        /// <param name="nonce">The nonce which satisfies given <paramref name="difficulty"/>.
        /// Goes to <see cref="Nonce"/>.</param>
        /// <param name="miner">The address of the miner.  Goes to <see cref="Miner"/>.</param>
        /// <param name="difficulty">The mining difficulty that <paramref name="nonce"/>
        /// has to satisfy.  Goes to <see cref="Difficulty"/>.</param>
        /// <param name="totalDifficulty">The total mining difficulty since the genesis,
        /// including the block's difficulty.  See also <see cref="Difficulty"/>.</param>
        /// <param name="previousHash">The previous block's <see cref="Hash"/>.  If it's a genesis
        /// block (i.e., its <see cref="Block{T}.Index"/> is 0) this should be <c>null</c>.
        /// Goes to <see cref="PreviousHash"/>.</param>
        /// <param name="txHash">The result of hashing the transactions the block has.
        /// Goes to <see cref="TxHash"/>.</param>
        /// <param name="hash">The hash digest derived from the whole contents of the block
        /// including <paramref name="stateRootHash"/>, which is determined by evaluating
        /// transactions.  This is used for block's unique identifier. Goes to <see cref="Hash"/>.
        /// </param>
        /// <param name="preEvaluationHash">The hash derived from the block <em>excluding</em>
        /// <paramref name="stateRootHash"/> (i.e., without action evaluation).
        /// Used for checking <paramref name="nonce"/>.  See also <see cref="Validate"/>.</param>
        /// <param name="stateRootHash">The <see cref="ITrie.Hash"/> of the resulting states after
        /// evaluating transactions and a block action (if exists).</param>
        /// <param name="hashAlgorithm">The hash algorithm used for PoW mining.</param>
        /// <remarks>
        /// This is only exposed for testing. Should not be used as an entry point to create
        /// a <see cref="BlockHeader"/> instance under normal circumstances.
        /// </remarks>
        public BlockHeader(
            int protocolVersion,
            long index,
            DateTimeOffset timestamp,
            Nonce nonce,
            Address miner,
            long difficulty,
            BigInteger totalDifficulty,
            BlockHash? previousHash,
            HashDigest<SHA256>? txHash,
            BlockHash hash,
            ImmutableArray<byte> preEvaluationHash,
            HashDigest<SHA256>? stateRootHash,
            HashAlgorithmType hashAlgorithm)
        {
            ProtocolVersion = protocolVersion;
            Index = index;
            Timestamp = timestamp.ToUniversalTime();
            Nonce = nonce;
            Miner = miner;
            Difficulty = difficulty;
            TotalDifficulty = totalDifficulty;
            PreviousHash = previousHash;
            TxHash = txHash;
            Hash = hash;
            PreEvaluationHash = preEvaluationHash;
            StateRootHash = stateRootHash;
            HashAlgorithm = hashAlgorithm;
        }

        /// <summary>
        /// Creates a <see cref="BlockHeader"/> instance from its serialization.
        /// </summary>
        /// <param name="hashAlgorithmGetter">The function to determine hash algorithm used for
        /// proof-of-work mining.</param>
        /// <param name="dict">The <see cref="Bencodex.Types.Dictionary"/>
        /// representation of <see cref="BlockHeader"/> instance.
        /// </param>
        public BlockHeader(HashAlgorithmGetter hashAlgorithmGetter, Bencodex.Types.Dictionary dict)
        {
            ProtocolVersion = dict.ContainsKey(ProtocolVersionKey)
                ? (int)dict.GetValue<Integer>(ProtocolVersionKey)
                : 0;
            Index = dict.GetValue<Integer>(IndexKey);
            Timestamp = DateTimeOffset.ParseExact(
                dict.GetValue<Text>(TimestampKey),
                TimestampFormat,
                CultureInfo.InvariantCulture
            ).ToUniversalTime();
            Difficulty = dict.GetValue<Integer>(DifficultyKey);
            TotalDifficulty = dict.GetValue<Integer>(TotalDifficultyKey);
            Nonce = new Nonce(dict.GetValue<Binary>(NonceKey).ByteArray);
            Miner = new Address(dict.GetValue<Binary>(MinerKey).ByteArray);

            PreviousHash = dict.ContainsKey((IKey)(Binary)PreviousHashKey)
                ? new BlockHash(dict.GetValue<Binary>(PreviousHashKey).ByteArray)
                : (BlockHash?)null;

            TxHash = dict.ContainsKey((IKey)(Binary)TxHashKey)
                ? new HashDigest<SHA256>(dict.GetValue<Binary>(TxHashKey).ByteArray)
                : (HashDigest<SHA256>?)null;

            PreEvaluationHash = dict.ContainsKey((IKey)(Binary)PreEvaluationHashKey)
                ? dict.GetValue<Binary>(PreEvaluationHashKey).ToImmutableArray()
                : ImmutableArray<byte>.Empty;

            StateRootHash = dict.ContainsKey((IKey)(Binary)StateRootHashKey)
                ? new HashDigest<SHA256>(dict.GetValue<Binary>(StateRootHashKey).ByteArray)
                : (HashDigest<SHA256>?)null;

            HashAlgorithm = hashAlgorithmGetter(Index);
            Hash = new BlockHash(dict.GetValue<Binary>(HashKey).ByteArray);
        }

        /// <summary>
        /// Creates a <see cref="BlockHeader"/> instance for a <see cref="Block{T}"/> instance with
        /// missing <see cref="Block{T}.StateRootHash"/>.
        /// </summary>
        /// <param name="protocolVersion">The protocol version.  Goes to
        /// <see cref="ProtocolVersion"/>.</param>
        /// <param name="index">The height of the block.  Goes to <see cref="Index"/>.
        /// </param>
        /// <param name="timestamp">The time the block is created.
        /// Goes to <see cref="Timestamp"/>.</param>
        /// <param name="nonce">The nonce which satisfies given <paramref name="difficulty"/>.
        /// Goes to <see cref="Nonce"/>.</param>
        /// <param name="miner">The address of the miner.  Goes to <see cref="Miner"/>.</param>
        /// <param name="difficulty">The mining difficulty that <paramref name="nonce"/>
        /// has to satisfy.  Goes to <see cref="Difficulty"/>.</param>
        /// <param name="totalDifficulty">The total mining difficulty since the genesis
        /// including the block's difficulty.  See also <see cref="Difficulty"/>.</param>
        /// <param name="previousHash">The previous block's <see cref="Hash"/>.  If it's a genesis
        /// block (i.e., its <see cref="Block{T}.Index"/> is 0) this should be <c>null</c>.
        /// Goes to <see cref="PreviousHash"/>.</param>
        /// <param name="txHash">The result of hashing the transactions the block has.
        /// Goes to <see cref="TxHash"/>.</param>
        /// <param name="hashAlgorithm">The proof-of-work hash algorithm.</param>
        internal BlockHeader(
            int protocolVersion,
            long index,
            DateTimeOffset timestamp,
            Nonce nonce,
            Address miner,
            long difficulty,
            BigInteger totalDifficulty,
            BlockHash? previousHash,
            HashDigest<SHA256>? txHash,
            HashAlgorithmType hashAlgorithm)
        {
            ProtocolVersion = protocolVersion;
            Index = index;
            Timestamp = timestamp.ToUniversalTime();
            Nonce = nonce;
            Miner = miner;
            Difficulty = difficulty;
            TotalDifficulty = totalDifficulty;
            PreviousHash = previousHash;
            TxHash = txHash;

            StateRootHash = null;
            HashAlgorithm = hashAlgorithm;
            byte[] serialized = SerializeForPreEvaluationHash();
            PreEvaluationHash = hashAlgorithm.Digest(serialized).ToImmutableArray();
            Hash = BlockHash.DeriveFrom(serialized);
        }

        /// <summary>
        /// Creates a <see cref="BlockHeader"/> instance for a <see cref="Block{T}"/>.
        /// </summary>
        /// <param name="protocolVersion">The protocol version.  Goes to
        /// <see cref="ProtocolVersion"/>.</param>
        /// <param name="index">The height of the block.  Goes to <see cref="Index"/>.
        /// </param>
        /// <param name="timestamp">The time the block is created.
        /// Goes to <see cref="Timestamp"/>.</param>
        /// <param name="nonce">The nonce which satisfies given <paramref name="difficulty"/>.
        /// Goes to <see cref="Nonce"/>.</param>
        /// <param name="miner">The address of the miner.  Goes to <see cref="Miner"/>.</param>
        /// <param name="difficulty">The mining difficulty that <paramref name="nonce"/>
        /// has to satisfy.  Goes to <see cref="Difficulty"/>.</param>
        /// <param name="totalDifficulty">The total mining difficulty since the genesis
        /// including the block's difficulty.  See also <see cref="Difficulty"/>.</param>
        /// <param name="previousHash">The previous block's <see cref="Hash"/>.  If it's a genesis
        /// block (i.e., its <see cref="Block{T}.Index"/> is 0) this should be <c>null</c>.
        /// Goes to <see cref="PreviousHash"/>.</param>
        /// <param name="txHash">The result of hashing the transactions the block has.
        /// Goes to <see cref="TxHash"/>.</param>
        /// <param name="preEvaluationHash">The hash derived from the block <em>excluding</em>
        /// <paramref name="stateRootHash"/> (i.e., without action evaluation).
        /// Used for checking <paramref name="nonce"/>.  See also <see cref="Validate"/>.</param>
        /// <param name="stateRootHash">The <see cref="ITrie.Hash"/> of the resulting states after
        /// evaluating transactions and a block action (if exists).</param>
        /// <param name="hashAlgorithm">The hash algorithm used for PoW mining.</param>
        internal BlockHeader(
            int protocolVersion,
            long index,
            DateTimeOffset timestamp,
            Nonce nonce,
            Address miner,
            long difficulty,
            BigInteger totalDifficulty,
            BlockHash? previousHash,
            HashDigest<SHA256>? txHash,
            ImmutableArray<byte> preEvaluationHash,
            HashDigest<SHA256>? stateRootHash,
            HashAlgorithmType hashAlgorithm
        )
        {
            // FIXME: Basic sanity check, such as whether stateRootHash is empty or not,
            // to prevent improper usage should be present. For the same reason as
            // a comment in Block<T>(), should be added in on further refactoring.
            ProtocolVersion = protocolVersion;
            Index = index;
            Timestamp = timestamp.ToUniversalTime();
            Nonce = nonce;
            Miner = miner;
            Difficulty = difficulty;
            TotalDifficulty = totalDifficulty;
            PreviousHash = previousHash;
            TxHash = txHash;

            PreEvaluationHash = preEvaluationHash;
            StateRootHash = stateRootHash;
            HashAlgorithm = hashAlgorithm;
            Hash = BlockHash.DeriveFrom(SerializeForHash());
        }

        /// <inheritdoc cref="IBlockMetadata.ProtocolVersion"/>
        public int ProtocolVersion { get; }

        /// <inheritdoc cref="IPreEvaluationBlockHeader.HashAlgorithm"/>
        public HashAlgorithmType HashAlgorithm { get; }

        /// <inheritdoc cref="IBlockMetadata.Index"/>
        public long Index { get; }

        /// <inheritdoc cref="IBlockMetadata.Timestamp"/>
        public DateTimeOffset Timestamp { get; }

        /// <inheritdoc cref="IPreEvaluationBlockHeader.Nonce"/>
        public Nonce Nonce { get; }

        /// <inheritdoc cref="IBlockMetadata.Miner"/>
        public Address Miner { get; }

        /// <inheritdoc cref="IBlockMetadata.Difficulty"/>
        public long Difficulty { get; }

        /// <inheritdoc cref="IBlockMetadata.TotalDifficulty"/>
        public BigInteger TotalDifficulty { get; }

        /// <inheritdoc cref="IBlockMetadata.PreviousHash"/>
        public BlockHash? PreviousHash { get; }

        /// <inheritdoc cref="IBlockMetadata.TxHash"/>
        public HashDigest<SHA256>? TxHash { get; }

        /// <summary>
        /// The hash digest derived from the whole contents of the block including
        /// <see cref="StateRootHash"/>, which is determined by evaluating transactions and
        /// a <see cref="Blockchain.Policies.IBlockPolicy{T}.BlockAction"/> (if exists).
        /// <para>This is used for block's unique identifier.</para>
        /// </summary>
        /// <seealso cref="PreEvaluationHash"/>
        /// <seealso cref="StateRootHash"/>
        public BlockHash Hash { get; }

        /// <inheritdoc cref="IPreEvaluationBlockHeader.PreEvaluationHash"/>
        public ImmutableArray<byte> PreEvaluationHash { get; }

        /// <summary>
        /// The <see cref="ITrie.Hash"/> of the resulting states after evaluating transactions and
        /// a <see cref="Blockchain.Policies.IBlockPolicy{T}.BlockAction"/> (if exists).
        /// </summary>
        /// <seealso cref="ITrie.Hash"/>
        public HashDigest<SHA256>? StateRootHash { get; }

        /// <summary>
        /// Gets <see cref="BlockHeader"/> instance from serialized <paramref name="bytes"/>.
        /// </summary>
        /// <param name="hashAlgorithmGetter">The function to determine hash algorithm used for
        /// proof-of-work mining.</param>
        /// <param name="bytes">Serialized <see cref="BlockHeader"/>.</param>
        /// <returns>Deserialized <see cref="BlockHeader"/>.</returns>
        /// <exception cref="DecodingException">Thrown when decoded value is not
        /// <see cref="Bencodex.Types.Dictionary"/> type.</exception>
        public static BlockHeader Deserialize(HashAlgorithmGetter hashAlgorithmGetter, byte[] bytes)
        {
            IValue value = Codec.Decode(bytes);
            if (!(value is Bencodex.Types.Dictionary dict))
            {
                throw new DecodingException(
                    $"Expected {typeof(Bencodex.Types.Dictionary)} but " +
                    $"{value.GetType()}");
            }

            return new BlockHeader(hashAlgorithmGetter, dict);
        }

        /// <summary>
        /// Gets serialized byte array of the <see cref="BlockHeader"/>.
        /// </summary>
        /// <returns>Serialized byte array of <see cref="BlockHeader"/>.</returns>
        public byte[] Serialize()
        {
            return new Codec().Encode(ToBencodex());
        }

        /// <summary>
        /// Gets <see cref="Bencodex.Types.Dictionary"/> representation of
        /// <see cref="BlockHeader"/>.
        /// </summary>
        /// <returns><see cref="Bencodex.Types.Dictionary"/> representation of
        /// <see cref="BlockHeader"/>.</returns>
        public Bencodex.Types.Dictionary ToBencodex()
        {
            string timestamp = Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            var dict = Bencodex.Types.Dictionary.Empty
                .Add(IndexKey, Index)
                .Add(TimestampKey, timestamp)
                .Add(DifficultyKey, Difficulty)
                .Add(TotalDifficultyKey, (IValue)(Bencodex.Types.Integer)TotalDifficulty)
                .Add(NonceKey, Nonce.ByteArray)
                .Add(MinerKey, Miner.ByteArray)
                .Add(HashKey, Hash.ByteArray);

            if (ProtocolVersion != 0)
            {
                dict = dict.Add(ProtocolVersionKey, ProtocolVersion);
            }

            if (PreviousHash is { } prev)
            {
                dict = dict.Add(PreviousHashKey, prev.ByteArray);
            }

            if (TxHash is { } th)
            {
                dict = dict.Add(TxHashKey, th.ByteArray);
            }

            if (PreEvaluationHash.Any())
            {
                dict = dict.Add(PreEvaluationHashKey, PreEvaluationHash.ToArray());
            }

            if (StateRootHash is { } rootHash)
            {
                dict = dict.Add(StateRootHashKey, rootHash.ByteArray);
            }

            return dict;
        }

        internal void Validate(HashAlgorithmType hashAlgorithm, DateTimeOffset currentTime)
        {
            if (ProtocolVersion < 0)
            {
                throw new InvalidBlockProtocolVersionException(
                    ProtocolVersion,
                    $"A block's protocol version cannot be less than zero: {ProtocolVersion}."
                );
            }
            else if (ProtocolVersion > CurrentProtocolVersion)
            {
                string message =
                    $"Unknown protocol version: {ProtocolVersion}; " +
                    $"the highest known version is {CurrentProtocolVersion}.";
                throw new InvalidBlockProtocolVersionException(ProtocolVersion, message);
            }

            this.ValidateTimestamp(currentTime);

            if (Index < 0)
            {
                throw new InvalidBlockIndexException(
                    $"Block #{Index} {Hash}'s index must be 0 or more."
                );
            }

            if (Difficulty > TotalDifficulty)
            {
                var msg = $"Block #{Index} {Hash}'s difficulty ({Difficulty}) " +
                          $"must be less than its TotalDifficulty ({TotalDifficulty}).";
                throw new InvalidBlockTotalDifficultyException(
                    Difficulty,
                    TotalDifficulty,
                    msg
                );
            }

            if (Index == 0)
            {
                if (Difficulty != 0)
                {
                    throw new InvalidBlockDifficultyException(
                        $"Difficulty must be 0 for the genesis block {Hash}, " +
                        $"but its difficulty is {Difficulty}."
                    );
                }

                if (TotalDifficulty != 0)
                {
                    var msg = "Total difficulty must be 0 for the genesis block " +
                              $"{Hash}, but its total difficulty is " +
                              $"{TotalDifficulty}.";
                    throw new InvalidBlockTotalDifficultyException(
                        Difficulty,
                        TotalDifficulty,
                        msg
                    );
                }

                if (PreviousHash is { })
                {
                    throw new InvalidBlockPreviousHashException(
                        $"Previous hash must be empty for the genesis block " +
                        $"{Hash}, but its value is {PreviousHash}."
                    );
                }
            }
            else
            {
                if (Difficulty < 1)
                {
                    throw new InvalidBlockDifficultyException(
                        $"Block #{Index} {Hash}'s difficulty must be more than 0 " +
                        $"(except of the genesis block), but its difficulty is {Difficulty}."
                    );
                }

                if (PreviousHash is null)
                {
                    throw new InvalidBlockPreviousHashException(
                        $"Block #{Index} {Hash}'s previous hash " +
                        "must be present since it's not the genesis block."
                    );
                }
            }

            if (!ByteUtil.Satisfies(PreEvaluationHash, Difficulty))
            {
                throw new InvalidBlockNonceException(
                    $"Block #{Index} {Hash}'s pre-evaluation hash " +
                    $"({ByteUtil.Hex(PreEvaluationHash)}) with nonce " +
                    $"({Nonce}) does not satisfy its difficulty level {Difficulty}."
                );
            }

            if (!hashAlgorithm.Equals(HashAlgorithm))
            {
                string msg =
                    $"Policy expects the block #{Index} to use {hashAlgorithm}, " +
                    $"but block #{Index} {Hash} uses {HashAlgorithm}.";
                throw new InvalidBlockPreEvaluationHashException(
                    PreEvaluationHash,
                    hashAlgorithm.Digest(SerializeForPreEvaluationHash()).ToImmutableArray(),
                    msg
                );
            }

            // PreEvaluationHash comparison between the actual and the expected was not
            // implemented in ProtocolVersion == 0.
            if (ProtocolVersion > 0)
            {
                byte[] expectedPreEvaluationHash =
                    HashAlgorithm.Digest(SerializeForPreEvaluationHash());
                if (!ByteUtil.TimingSafelyCompare(expectedPreEvaluationHash, PreEvaluationHash))
                {
                    string message =
                        $"The expected pre-evaluation hash of block #{Index} " +
                        $"{Hash} is {ByteUtil.Hex(expectedPreEvaluationHash)}, " +
                        $"but its pre-evaluation hash is {ByteUtil.Hex(PreEvaluationHash)}.";
                    throw new InvalidBlockPreEvaluationHashException(
                        PreEvaluationHash,
                        expectedPreEvaluationHash.ToImmutableArray(),
                        message);
                }
            }

            BlockHash expectedHash = BlockHash.DeriveFrom(SerializeForHash());
            if (!Hash.Equals(expectedHash))
            {
                throw new InvalidBlockHashException(
                    $"The expected hash {expectedHash} of block #{Index} {Hash} does not match " +
                    "the hash provided by the block.");
            }
        }

        // FIXME: This method should be replaced by BlockContent<T>.ToBencodex() method.
        internal Bencodex.Types.Dictionary ToBencodexForPreEvaluationHash()
        {
            // TODO: Include TotalDifficulty as well
            var dict = Bencodex.Types.Dictionary.Empty
                .Add("index", Index)
                .Add("timestamp", Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture))
                .Add("difficulty", Difficulty)
                .Add("nonce", Nonce.ByteArray)
                .Add("reward_beneficiary", Miner.ByteArray);

            if (ProtocolVersion != 0)
            {
                dict = dict.Add("protocol_version", ProtocolVersion);
            }

            if (PreviousHash is { } prevHash)
            {
                dict = dict.Add("previous_hash", prevHash.ByteArray);
            }

            if (TxHash is { } txHash)
            {
                dict = dict.Add("transaction_fingerprint", txHash.ByteArray);
            }

            return dict;
        }

        internal Bencodex.Types.Dictionary ToBencodexForHash()
        {
            var dict = ToBencodexForPreEvaluationHash();

            if (StateRootHash is { } rootHash)
            {
                dict = dict.Add("state_root_hash", rootHash.ByteArray);
            }

            return dict;
        }

        internal byte[] SerializeForPreEvaluationHash()
            => new Codec().Encode(ToBencodexForPreEvaluationHash());

        internal byte[] SerializeForHash()
            => new Codec().Encode(ToBencodexForHash());
    }
}
