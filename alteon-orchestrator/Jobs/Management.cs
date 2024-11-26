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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.PKI.X509;
using Microsoft.Extensions.Logging;


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
            string pemCert, pemKey;

            try
            {
                bytes = Convert.FromBase64String(entryContents);
                x509 = new X509Certificate2(bytes, pfxPassword, X509KeyStorageFlags.Exportable);
                (pemCert, pemKey) = GetPemFromPfx(bytes, pfxPassword);
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
                        await aClient.AddCertificate(alias, pfxPassword, pemKey, AlteonCertTypes.KEY_ONLY, Overwrite);
                        await aClient.AddCertificate(alias, pfxPassword, pemCert, AlteonCertTypes.CERT_ONLY, Overwrite);
                    }
                    else
                    {
                        logger.LogTrace($"Adding certificate only for certificate with alias {alias}");
                        await aClient.AddCertificate(alias, pfxPassword, pemCert, certType, Overwrite);
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

        private (string, string) GetPemFromPfx(byte[] pfxBytes, string pfxPassword)
        {
            try
            {
                logger.MethodEntry();

                CertificateCollectionConverter converter = CertificateCollectionConverterFactory.FromDER(pfxBytes, pfxPassword);
                string pfxPem = converter.ToPEM(pfxPassword);
                List<X509Certificate2> clist = converter.ToX509Certificate2List(pfxPassword);
                StringBuilder certPemBuilder = new StringBuilder();

                //reordering of certificate chain necessary because of BouncyCastle bug.  Being fixed in a later release
                if (clist.Count > 1)
                    clist = ReorderPEMLIst(clist);

                logger.LogTrace("Building certificate PEM");
                foreach (X509Certificate2 cert in clist)
                {
                    certPemBuilder.AppendLine("-----BEGIN CERTIFICATE-----");
                    certPemBuilder.AppendLine(
                        Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks));
                    certPemBuilder.AppendLine("-----END CERTIFICATE-----");
                }

                logger.LogTrace("Building the key PEM");
                byte[] pkBytes = PKI.PrivateKeys.PrivateKeyConverterFactory.FromPKCS12(pfxBytes, pfxPassword.ToString()).ToPkcs8BlobUnencrypted();
                StringBuilder keyPemBuilder = new StringBuilder();
                keyPemBuilder.AppendLine("-----BEGIN PRIVATE KEY-----");
                keyPemBuilder.AppendLine(
                    Convert.ToBase64String(pkBytes, Base64FormattingOptions.InsertLineBreaks));
                keyPemBuilder.AppendLine("-----END PRIVATE KEY-----");

                logger.LogTrace($"certPem: {certPemBuilder}");
                logger.MethodExit();
                return (certPemBuilder.ToString(), keyPemBuilder.ToString());
            }
            catch (Exception e)
            {
                logger.LogError(
                    $"Error Occurred in GetPemFromPfx(byte[] pfxBytes, string pfxPassword): {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        private List<X509Certificate2> ReorderPEMLIst(List<X509Certificate2> certList)
        {
            List<X509Certificate2> rtnList = new List<X509Certificate2>();
            X509Certificate2 root = certList.FirstOrDefault(p => p.IssuerName.RawData.SequenceEqual(p.SubjectName.RawData));
            if (root == null || string.IsNullOrEmpty(root.SerialNumber))
                throw new Exception("Invalid certificate chain.  No root CA certificate found.");

            rtnList.Add(root);

            X509Certificate2 parentCert = root;
            for (int i = 1; i < certList.Count; i++)
            {
                X509Certificate2 childCert = certList.FirstOrDefault(p => p.IssuerName.RawData.SequenceEqual(parentCert.SubjectName.RawData) && !p.IssuerName.RawData.SequenceEqual(p.SubjectName.RawData));
                if (root == null || string.IsNullOrEmpty(root.SerialNumber))
                    throw new Exception("Invalid certificate chain.  End entity or issuing CA certificate not found.");

                rtnList.Insert(0, childCert);
                parentCert = childCert;
            }

            return rtnList;
        }
    }
}
