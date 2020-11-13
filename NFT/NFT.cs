using Quras.SmartContract.Framework;
using Quras.SmartContract.Framework.Services.Module;
using Quras.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NFT
{
    public class NFT : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], byte[]> Transferred;
        [DisplayName("approval")]
        public static event Action<byte[], byte[], byte[]> Approval;

        private static readonly byte[] Owner = "DX4TWAha5pM9hVsxD4QRoyac1ubHLMJXqC".ToScriptHash(); //Owner Address
        private static readonly BigInteger TotalSupplyValue = 10000000000000000;

        private static readonly String version = "v1.1";

        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(Owner);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                
                if (method == "balanceOf") return BalanceOf((byte[])args[0]);

                if (method == "decimals") return Decimals();

                if (method == "name") return Name();

                if (method == "symbol") return Symbol();

                if (method == "supportedStandards") return SupportedStandards();

                if (method == "totalSupply") return TotalSupply();

                if (method == "deploys") return Deploys();

                if (method == "mint") return Mint((byte[])args[0], ((byte[])args[1]).AsString());

                if (method == "transfer") return Transfer((byte[])args[0], (byte[])args[1], callscript);

                if (method == "ownerOf") return OwnerOf((byte[])args[0]);

                if (method == "approve") return Approve((byte[])args[0], (byte[])args[1], callscript);

                if (method == "takeOwnership") return TakeOwnership((byte[])args[0], callscript);

                if (method == "tokenOfOwnerByIndex") return TokenOfOwnerByIndex((byte[])args[0], ((byte[])args[1]).AsBigInteger());

                if (method == "tokenMetadata") return TokenMetadata((byte[])args[0]);

                if (method == "version") return Version();
            }
            return false;
        }

        [DisplayName("balanceOf")]
        public static BigInteger BalanceOf(byte[] address)
        {
            if (address.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte array.");
            StorageMap balances = Storage.CurrentContext.CreateMap(nameof(balances));
            return balances.Get(address).AsBigInteger();
        }

        [DisplayName("decimals")]
        public static byte Decimals() => 8;

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }

        [DisplayName("totalSupply")]
        public static BigInteger TotalSupply()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            if (contract.Get("totalSupply").AsBigInteger() == 0)
                contract.Put("totalSupply", TotalSupplyValue);
            return contract.Get("totalSupply").AsBigInteger();
        }

        [DisplayName("deploys")]
        public static BigInteger Deploys()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("deploys").AsBigInteger();
        }

        [DisplayName("mint")]
        public static bool Mint(byte[] to, string meta)
        {
            if (to.Length != 20)
                throw new InvalidOperationException("The parameters to SHOULD be 20-byte array.");
            byte[] tokenId = (Deploys() + 1).AsByteArray();
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            if (OwnerOf(tokenId) != null)
                return false;
            if (tokenId.AsBigInteger() > TotalSupplyValue)
                return false;
            if (!Runtime.CheckWitness(to))
                return false;

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("deploys", tokenId);

            StorageMap balances = Storage.CurrentContext.CreateMap(nameof(balances));
            BigInteger amount = 1;

            //Increase the payee balance
            var toAmount = balances.Get(to).AsBigInteger();
            balances.Put(to, toAmount + amount);

            StorageMap tokenOwners = Storage.CurrentContext.CreateMap(nameof(tokenOwners));
            tokenOwners.Put(tokenId, to);

            AddToTokenList(to, tokenId);

            StorageMap tokenLinks = Storage.CurrentContext.CreateMap(nameof(tokenLinks));
            tokenLinks.Put(tokenId, meta);

            Transferred(null, to, tokenId);
            return true;
        }

        [DisplayName("name")]
        public static string Name() => "Non-Fungible Token Template"; //name of the token

        [DisplayName("symbol")]
        public static string Symbol() => "NFT"; //symbol of the token

        [DisplayName("supportedStandards")]
        public static string[] SupportedStandards() => new string[] { "NEP-5", "NEP-7", "NEP-10" };

        [DisplayName("transfer")]
        private static bool Transfer(byte[] to, byte[] tokenId, byte[] callscript)
        {
            if (to.Length != 20)
                throw new InvalidOperationException("The parameters to SHOULD be 20-byte array.");
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            byte[] from = OwnerOf(tokenId);
            if (from == null || callscript != from)
                return false;
            if (from == to)
                return true;

            StorageMap balances = Storage.CurrentContext.CreateMap(nameof(balances));
            BigInteger amount = 1;

            var fromAmount = balances.Get(from).AsBigInteger();
            if (fromAmount < amount)
                return false;

            //Reduce payer balances
            if (fromAmount == amount)
                balances.Delete(from);
            else
                balances.Put(from, fromAmount - amount);

            //Increase the payee balance
            var toAmount = balances.Get(to).AsBigInteger();
            balances.Put(to, toAmount + amount);

            StorageMap tokenOwners = Storage.CurrentContext.CreateMap(nameof(tokenOwners));
            tokenOwners.Put(tokenId, to);

            RemoveFromTokenList(from, tokenId);

            Transferred(from, to, tokenId);
            return true;
        }

        [DisplayName("ownerOf")]
        public static byte[] OwnerOf(byte[] tokenId)
        {
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            StorageMap tokenOwners = Storage.CurrentContext.CreateMap(nameof(tokenOwners));
            return tokenOwners.Get(tokenId);
        }

        [DisplayName("approve")]
        public static bool Approve(byte[] to, byte[] tokenId, byte[] callscript)
        {
            if (to.Length != 20)
                throw new InvalidOperationException("The parameter to SHOULD be 20-byte array.");
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            byte[] from = OwnerOf(tokenId);
            if (from == null || callscript != from)
                return false;
            if (callscript == to)
                return false;

            StorageMap allowed = Storage.CurrentContext.CreateMap(nameof(allowed));
            allowed.Put(from.Concat(to), tokenId);

            Approval(from, to, tokenId);
            return true;
            
        }

        [DisplayName("takeOwnership")]
        public static bool TakeOwnership(byte[] tokenId, byte[] callscript)
        {
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            byte[] from = OwnerOf(tokenId);
            if (from == null)
                return false;
            byte[] to = callscript;

            if (from == to)
                return true;

            StorageMap allowed = Storage.CurrentContext.CreateMap(nameof(allowed));
            if (allowed.Get(from.Concat(to)) != tokenId)
                return false;

            StorageMap balances = Storage.CurrentContext.CreateMap(nameof(balances));
            BigInteger amount = 1;

            var fromAmount = balances.Get(from).AsBigInteger();
            if (fromAmount < amount)
                return false;

            //Reduce payer balances
            if (fromAmount == amount)
                balances.Delete(from);
            else
                balances.Put(from, fromAmount - amount);

            //Increase the payee balance
            var toAmount = balances.Get(to).AsBigInteger();
            balances.Put(to, toAmount + amount);

            StorageMap tokenOwners = Storage.CurrentContext.CreateMap(nameof(tokenOwners));
            tokenOwners.Put(tokenId, to);

            RemoveFromTokenList(from, tokenId);

            Transferred(from, to, tokenId);
            return true;
        }

        [DisplayName("tokenOfOwnerByIndex")]
        public static byte[] TokenOfOwnerByIndex(byte[] address, BigInteger index)
        {
            if (address.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte array.");
            if (index <= 0)
                index = 1;
            StorageMap ownerTokens = Storage.CurrentContext.CreateMap(nameof(ownerTokens));
            return ownerTokens.Get(address.Concat(index.AsByteArray()));
        }

        [DisplayName("tokenMetadata")]
        public static byte[] TokenMetadata(byte[] tokenId)
        {
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            StorageMap tokenLinks = Storage.CurrentContext.CreateMap(nameof(tokenLinks));
            return tokenLinks.Get(tokenId);
        }

        [DisplayName("version")]
        public static string Version()
        {
            return version;
        }

        private static bool AddToTokenList(byte[] address, byte[] tokenId)
        {
            if (address.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte array.");
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            BigInteger balance = BalanceOf(address);
            StorageMap ownerTokens = Storage.CurrentContext.CreateMap(nameof(ownerTokens));
            ownerTokens.Put(address.Concat(balance.AsByteArray()), tokenId);
            return true;
        }

        private static bool RemoveFromTokenList(byte[] address, byte[] tokenId)
        {
            if (address.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte array.");
            if (tokenId.Length <= 0)
                throw new InvalidOperationException("The parameter tokenId SHOULD be byte array.");
            StorageMap ownerTokens = Storage.CurrentContext.CreateMap(nameof(ownerTokens));
            byte[] deletedKey = null;
            for (BigInteger i = 1; i <= TotalSupplyValue; i=i+1)
            {
                byte[] key = address.Concat(i.AsByteArray());
                if (ownerTokens.Get(key) == null)
                    break;
                if (deletedKey != null)
                {
                    ownerTokens.Put(deletedKey, ownerTokens.Get(key));
                    ownerTokens.Delete(key);
                    deletedKey = key;
                    continue;
                }
                if (ownerTokens.Get(key) == tokenId)
                {
                    ownerTokens.Delete(key);
                    deletedKey = key;
                }
            }
                
            return deletedKey != null;
        }
    }
}
