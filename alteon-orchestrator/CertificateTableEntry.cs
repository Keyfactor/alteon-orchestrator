using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.AlteonLoadBalancer
{
    public class CertificateTableEntry
    {
        public string ID { get; set; }
        public int? Type { get; set; }
        public string Name { get; set; }
        public int? KeySize { get; set; } // {1=ks512 2=ks1024 3=ks2048 4=ks4096 6=unknown }
        public string Expirty { get; set; }
        public string CommonName { get; set; }
        public int? HashAlgo { get; set; } // {1=md5 2=sha1 3=sha256 4=sha384 5=sha512 6=unknown }
        public string CountryName { get; set; }
        public string PrpvinceName { get; set; }
        public string LocalityName { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationUnitName { get; set; }
        public string EMail { get; set; }
        public int? ValidityPeriod { get; set; }
        public int? DeleteStatus { get; set; } // {1=other 2=delete }
        public int? Generate { get; set; } // {1=other 2=generate 3=idle 4=inprogress 5=generated 6=notGenerated }
        public int? Status { get; set; } // {1=generated 2=notGenerated 3=inProgress }
        public int? KeyType { get; set; } // {1=rsa 2=ec 6=unknown }
        public int? KeySizeEc { get; set; } // {0=ks0 1=ks192 2=ks224 3=ks256 4=ks384 5=ks521 6=unknown }
        public int? CurveNameEc { get; set; } // {1=secp112r1 2=secp112r2 3=secp128r1 4=secp128r2 5=secp160k1 6=secp160r1 7=secp160r2 8=secp192k1 9=secp224k1 10=secp224r1 11=secp256k1 12=secp384r1 13=secp521r1 14=prime192v1 15=prime192v2 16=prime192v3 17=prime239v1 18=prime239v2 19=prime239v3 20=prime256v1 21=sect113r1 22=sect113r2 23=sect131r1 24=sect131r2 25=sect163k1 26=sect163r1 27=sect163r2 28=sect193r1 29=sect193r2 30=sect233k1 31=sect233r1 32=sect239k1 33=sect283k1 34=sect283r1 35=sect409k1 36=sect409r1 37=sect571k1 38=sect571r1 39=c2pnb163v1 40=c2pnb163v2 41=c2pnb163v3 42=c2pnb176v1 43=c2tnb191v1 44=c2tnb191v2 45=c2tnb191v3 46=c2pnb208w1 47=c2tnb239v1 48=c2tnb239v2 49=c2tnb239v3 50=c2pnb272w1 51=c2pnb304w1 52=c2tnb359v1 53=c2pnb368w1 54=c2tnb431r1 55=wtls1 56=wtls3 57=wtls4 58=wtls5 59=wtls6 60=wtls7 61=wtls8 62=wtls9 63=wtls10 64=wtls11 65=wtls12 0=unknown }
        public int? KeySizeCommon { get; set; }
    }

    public class CertificateTableEntryCollection
    {
        public List<CertificateTableEntry> SlbNewSslCfgCertsTable { get; set; }
    }
}
