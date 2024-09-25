// Copyright 2022 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;


namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer.Jobs
{
    public class Management : JobBase, IManagementJobExtension
    {
        readonly ILogger logger = LogHandler.GetClassLogger<Management>();

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            InitializeStore(config);

            JobResult complete = new JobResult()
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage = "Invalid Management Operation"
            };

            switch (config.OperationType)
            {
                case CertStoreOperationType.Add:
                    logger.LogDebug($"Begin Management > Add...");
                    complete = PerformAddition(config.JobCertificate.Alias, config.JobCertificate.PrivateKeyPassword, config.JobCertificate.Contents, config.JobHistoryId).Result;
                    break;
                case CertStoreOperationType.Remove:
                    logger.LogDebug($"Begin Management > Remove...");
                    complete = PerformRemoval(config.JobCertificate.Alias, config.JobHistoryId).Result;
                    break;
            }

            return complete;
        }

        protected virtual async Task<JobResult> PerformAddition(string alias, string pfxPassword, string entryContents, long jobHistoryId)
        {
            var complete = new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = jobHistoryId };

            /// there are three types of certificates we can add from the Keyfactor platform
            /// if there is a cert and key, import as "pair"
            /// if there is no key, and the issuer and subject match, import as "clca" (trusted CA)
            /// if there is no key, and the issuer and subject are different, import as "inca" (intermediate CA)
            
            byte[] bytes;
            X509Certificate2 x509;
            string pemCert, privateKeyString;

            try
            {
                bytes = Convert.FromBase64String(entryContents);
                x509 = new X509Certificate2(bytes, pfxPassword, X509KeyStorageFlags.Exportable);
                (pemCert, privateKeyString) = GetPemFromPfx(bytes, pfxPassword.ToCharArray());
            }
            catch (Exception ex)
            {
                logger.LogError("error decoding certificate", ex);
                throw;
            }

            var certType = AlteonCertTypes.INTERMEDIATE_CA;

            if (x509.PrivateKey != null)
            {
                logger.LogTrace($"Private key is present, setting cert type to {AlteonCertTypes.CERTIFICATE_AND_KEY}");
                certType = AlteonCertTypes.CERTIFICATE_AND_KEY; // we import as a pair
            }
            else
            {
                if (x509.Subject == x509.Issuer)
                {
                    logger.LogTrace($"Subject = {x509.Issuer}, importing as a trusted CA certificate");
                    certType = AlteonCertTypes.TRUSTED_CA; // we import as a trusted ca
                }
                // else we import as intermediate ca (default)                
            }

            logger.LogTrace($"determined type to be {certType}");

            if (!string.IsNullOrWhiteSpace(pfxPassword)) // This is a PFX Entry
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    complete.FailureMessage = "You must supply an alias for the certificate.";
                    return complete;
                }

                try
                {
                    if (certType == AlteonCertTypes.CERTIFICATE_AND_KEY)
                    {
                        // add key and cert separately.  
                        // this needs to be done in the following order: key, then cert (per Alteon support)                        
                        logger.LogTrace($"adding key and then certificate for certificate with alias {alias}");
                        await aClient.AddCertificate(alias, pfxPassword, Pemify(privateKeyString), AlteonCertTypes.KEY_ONLY);
                        await aClient.AddCertificate(alias, pfxPassword, pemCert, AlteonCertTypes.CERT_ONLY);
                    }
                    else
                    {
                        logger.LogTrace($"Adding certificate only for certificate with alias {alias}");
                        await aClient.AddCertificate(alias, pfxPassword, pemCert, certType);
                    }
                    complete.Result = OrchestratorJobStatusJobResult.Success;
                }
                catch (Exception ex)
                {
                    complete.FailureMessage = $"An error occured while adding {alias} to {ExtensionName}: " + ex.Message;

                    if (ex.InnerException != null)
                        complete.FailureMessage += " - " + ex.InnerException.Message;
                    logger.LogError($"an error occurred when attempting to add certificate: {ex.Message}");
                }
            }

            else  // Non-PFX
            {
                complete.FailureMessage = "Certificate to add must be in a .PFX file format.";
            }

            return complete;
        }

        protected virtual async Task<JobResult> PerformRemoval(string alias, long jobHistoryId)
        {
            JobResult complete = new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = jobHistoryId };

            if (string.IsNullOrWhiteSpace(alias))
            {
                complete.FailureMessage = "You must supply an alias for the certificate.";
                return complete;
            }

            try
            {
                await aClient.RemoveCertificate(alias);
                complete.Result = OrchestratorJobStatusJobResult.Success;                
            }

            catch (Exception ex)
            {
                logger.LogError("Error deleting cert from device.", ex);
                complete.FailureMessage = $"An error occured while removing {alias} from {ExtensionName}: " + ex.Message;
            }
            return complete;
        }

        private (string, string) GetPemFromPfx(byte[] pfxBytes, char[] pfxPassword)
        {
            try
            {
                logger.LogDebug("Entering GetPemFromPfx(byte[] pfxBytes, char[] pfxPassword)");
                var p = new Pkcs12Store(new MemoryStream(pfxBytes), pfxPassword);

                // Extract private key
                var memoryStream = new MemoryStream();
                TextWriter streamWriter = new StreamWriter(memoryStream);
                var pemWriter = new PemWriter(streamWriter);

                var alias = p.Aliases.Cast<string>().SingleOrDefault(a => p.IsKeyEntry(a));
                logger.LogTrace($"alias: {alias}");

                var publicKey = p.GetCertificate(alias).Certificate.GetPublicKey();
                if (p.GetKey(alias) == null) throw new Exception($"Unable to get the key for alias: {alias}");
                var privateKey = p.GetKey(alias).Key;
                var keyPair = new AsymmetricCipherKeyPair(publicKey, privateKey);

                pemWriter.WriteObject(keyPair.Private);
                streamWriter.Flush();
                var privateKeyString = Encoding.ASCII.GetString(memoryStream.GetBuffer()).Trim().Replace("\r", "")
                    .Replace("\0", "");
                memoryStream.Close();
                streamWriter.Close();

                // Extract server certificate
                var certStart = "-----BEGIN CERTIFICATE-----\n";
                var certEnd = "\n-----END CERTIFICATE-----";

                string Pemify(string ss)
                {
                    return ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + Pemify(ss.Substring(64));
                }

                var certPem =
                    certStart + Pemify(Convert.ToBase64String(p.GetCertificate(alias).Certificate.GetEncoded())) +
                    certEnd;
                logger.LogTrace($"certPem: {certPem}");
                logger.LogDebug("Exiting GetPemFromPfx(byte[] pfxBytes, char[] pfxPassword)");
                return (certPem, privateKeyString);
            }
            catch (Exception e)
            {
                logger.LogError(
                    $"Error Occurred in GetPemFromPfx(byte[] pfxBytes, char[] pfxPassword): {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        private string Pemify(string ss)
        {
            return ss.Length <= 64 ? ss : ss.Substring(0, 64) + "\n" + Pemify(ss.Substring(64));
        }

    }
}
