﻿using Microsoft.Azure;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Core;
using Microsoft.Azure.KeyVault.WebKey;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptBitLibrary
{
    public static class CommonHelper
    {


        public static string SerializeToJson(object o)
        {
            return JsonConvert.SerializeObject(o);
        }
        public static T DeserializeJsonToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async static Task<string> GetToken(string authority, string resource, string scope)
        {
            var authContext = new AuthenticationContext(authority);
            ClientCredential clientCred = new ClientCredential(
                CloudConfigurationManager.GetSetting("aadClientId"),
               CloudConfigurationManager.GetSetting("aadClientSecret"));
            AuthenticationResult result = await authContext.AcquireTokenAsync(resource, clientCred);

            if (result == null)
                throw new InvalidOperationException("Failed to obtain the JWT token");

            return result.AccessToken;
        }

        public async static Task<IKey> ResolveKey(string uri)
        {
            IKey rsa = null;

            try
            {
                KeyVaultKeyResolver cloudResolver = new KeyVaultKeyResolver(GetToken);
                rsa = await cloudResolver.ResolveKeyAsync(uri, CancellationToken.None);
            } catch (Exception ex)
            {
                Logger.TrackException(ex, 0, "Error fetching key from KeyVault");
                throw ex;
            }
            return rsa;
        }
        
        public async static Task<KeyBundle> CreateKeyBundle(string keyName)
        {
            KeyBundle createdKey = null;

            try
            {
                string vault = CloudConfigurationManager.GetSetting("kvVaultUri");
                KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(GetToken));
                createdKey = await keyVaultClient.CreateKeyAsync(vault, keyName, JsonWebKeyType.Rsa);
            } catch (Exception ex)
            {
                Logger.TrackException(ex, 0, "Can't create key in KeyVault");

            }

            return createdKey;
        }

        public async static Task<string> CreateKey(string keyName)
        {
           KeyBundle key =  await CreateKeyBundle(keyName);

            return key.KeyIdentifier.Identifier;
            
          
        }

    }
}
