## Overview

## Setup and Configuration

The high level steps required to configure the Alteon Load Balancer Orchestrator extension are:

1) [Create the Store Type in Keyfactor](#create-the-store-type-in-keyfactor)

1) [Install the Extension on the Orchestrator](#install-the-extension-on-the-orchestrator)

1) [Create the Certificate Store](#create-the-certificate-store)

---

### Create the Store Type in Keyfactor

Now we can navigate to the Keyfactor platform and create the store type for the extension.

1) Navigate to your instance of Keyfactor and log in with a user that has Administrator priveledges.

1) Click on the gear icon in the top left and navigate to "Certificate Store Types".

     ![Cert Store Types Menu](/images/store-types-menu.png)

1) Click "Add" to open the Add Certificate Store dialog.

1) Name the new store type "Alteon Load Balancer" and give it the short name of "AlteonLB".

1) The Alteon Load Balancer integration supports the following job types: _Inventory, Add, Remove_.  Select from these the capabilities you would like to utilize.

1) Make sure that "Needs Server" is checked.

     ![Cert Store Types Menu](/images/add-store-type.png)


1) Set the following values on the __Advanced__ tab:
     1) **Supports Custom Alias** - Optional
     1) **Private Key Handling** - Optional

     ![Cert Store Types Advanced](/images/store-type-advanced.png)

1) On the __Entry Parameters__ tab, add the following parameter:

   | Name | Display Name | Type | Required for Add | Required for Remove | Description |
   | ---- | ------------ | ---- | :--------------: | :-----------------: | ----------- |
   | `VirtualServiceBindings` | Virtual Service Bindings | String | ❌ | ❌ | Comma-separated list of virtual service bindings in `virtId:servicePort` format. Specifies which virtual services the certificate should be bound to. See [Virtual Service Bindings](#virtual-service-bindings) for details. |

### Install the Extension on the Orchestrator

_The process for installing an extension for the universal orchestrator differs from the process of installing an extension for the Windows orchestrator.  Follow the below steps to register the integration with your instance of the universal orchestrator._

1) Stop the Universal Orchestrator service.

     1) Note: In Windows, the default name of this service is "Keyfactor Orchestrator Service (Default)"

1) Create a folder in the "extensions" folder of the Universal Orchestrator installation folder named "AlteonLB"

     1) example: `C:\Program Files\Keyfactor\Keyfactor Orchestrator\\_AlteonLB_`

1) Copy the build output (if you compiled from source) or the contents of the zip file (if you downloaded the pre-compiled binaries) into this folder.

1) Start the Universal Orchestrator Service


### Create the certificate store

Now add the certificate store that corresponds to an instance of the Alteon Load Balancer.

The steps to do this are:

1) Navigate to "Locations > Certificate Stores"

1) Click "ADD"

     ![Approve Cert Store](/images/add-cert-store-button.png)

1) Enter the values corresponding to the Alteon Load Balancer instance.

- **Category**: Alteon Load Balancer
- **Container**: _optional logical container in keyfactor for the certificates from this store_
- **Client Machine**: The Alteon Load Balancer Server and port

  - Note: The server credentials will only have to be entered once, even if adding multiple certificate stores.  
    - Set the credentials to those of the account with sufficient permissions to manage certs in the Alteon Load Balancer.
    - Check __Use SSL__
    - The __Server Name__ should be the fully qualified URL and port of the Alteon Load Balancer instance.

![Server Credentials](/images/client-credentials.png)

- **Store Path**: This value isn't used for this integration (other than to uniquely identify the cert store in certificate searches).  

---

## Virtual Service Bindings

This integration supports binding certificates to one or more Alteon virtual services as part of the certificate enrollment (Add) workflow. The **Virtual Service Bindings** entry parameter controls which virtual services a certificate is bound to, and the inventory job returns this information so that Keyfactor Command maintains an accurate view of where each certificate is deployed.

### Entry Parameter Format

The `VirtualServiceBindings` entry parameter accepts a comma-separated list of bindings, where each binding is expressed as:

```
virtId:servicePort
```

| Component | Description | Example |
| --------- | ----------- | ------- |
| `virtId` | The virtual server ID as configured on the Alteon device | `1`, `my-virt` |
| `servicePort` | The TCP port of the HTTPS service on that virtual server | `443`, `8443` |

**Examples:**

```
# Single binding
1:443

# Multiple bindings — same certificate on three virtual services
1:443,2:443,my-virt:8443
```

> **Note:** Virtual server IDs containing colons (`:`) or commas (`,`) are not supported in the `VirtualServiceBindings` parameter, as these characters are used as delimiters. The service port must be a valid TCP port number between 1 and 65535. Alteon virtual server IDs consisting of alphanumeric characters, hyphens, and underscores are fully supported.

> **Note:** The virtual service must already exist on the Alteon device before running an Add job. This integration manages certificate bindings on existing services; it does not create virtual services.

### SNI and Non-SNI Support

The integration automatically detects how each virtual service is configured for SSL and handles the binding accordingly — no additional configuration is required.

**Non-SNI (direct binding):** When a virtual service serves a single domain, SSL is terminated using a single certificate bound directly to that service. The integration sets the `SrvCert` field on the virtual service to the certificate being enrolled.

**SNI (Server Name Indication):** When a virtual service hosts multiple domains on the same IP address and port, [SNI](https://www.cloudflare.com/learning/ssl/what-is-sni/) allows it to present the correct certificate to each client based on the domain name the client requests. In this configuration, certificates are managed through a *certificate group* rather than being bound directly to the virtual service. The integration detects this automatically by inspecting the virtual service configuration and adds the enrolled certificate to the appropriate group.

In both cases, the `VirtualServiceBindings` entry parameter format is identical — the integration determines which path to take based on the current state of the virtual service on the device.

### SSL Policy Management

When binding a certificate to a virtual service in non-SNI mode, an SSL policy must be associated with the service to govern the SSL handshake behaviour (permitted TLS versions, cipher suites, and so on). The integration manages this automatically using the following convention:

**Policy naming:** `KF-{virtId}-{servicePort}`

For example, a binding of `1:443` will use an SSL policy named `KF-1-443`.

If a policy with this name does not already exist on the device, the integration creates one with the following security defaults:

| Setting | Default Value |
| ------- | ------------- |
| Frontend SSL | Enabled |
| Backend SSL | Disabled |
| Cipher Suite | HIGH |
| TLS 1.2 | Enabled |
| TLS 1.3 | Enabled |
| TLS 1.0 / 1.1 | Disabled |

If a policy with this name already exists — for example, from a previous enrollment or from a policy created manually with the same name — the existing policy is left unchanged. This preserves any custom settings an administrator may have applied.

> **Note:** SSL policy management applies only to non-SNI virtual services. In SNI mode, the certificate group configuration governs SSL behaviour and no policy is created or modified by this integration.

### Removing Certificates

Before a certificate can be removed from the Alteon certificate repository, it must not be actively bound to any virtual service. If the certificate is currently bound, the Remove job will fail with a descriptive error identifying the affected virtual service(s):

> *Cannot remove cert 'my-cert' — it is currently bound to virtual service(s): webssl:443. Bind a replacement certificate to those service(s) first using an Add with Overwrite enabled, or clear the binding(s) manually in the Alteon UI before removing.*

This restriction exists because removing a certificate that is actively serving SSL traffic would leave the virtual service with no certificate, immediately breaking SSL termination on that service — and potentially terminating the REST API connection used to manage the device itself.

To remove a certificate that is currently bound to one or more virtual services, choose one of the following approaches:

- **Replace it first (recommended):** Enroll a new certificate using an Add job with the same `VirtualServiceBindings` value and Overwrite enabled. Once the new certificate is bound, the old one can be safely removed.
- **Clear the binding manually:** In the Alteon management UI, navigate to the virtual service and clear the certificate binding, then re-run the Remove job.

> **Note:** For SNI certificate groups, an additional restriction applies: a certificate that is the *default* certificate for a group cannot be removed. The default cert must be reassigned to another member of the group in the Alteon UI before removal.

### Overwrite Behaviour

If a virtual service already has a **different** certificate bound to it:

- **Overwrite disabled (default):** The Add job fails with a descriptive error identifying the virtual service and the certificate currently bound to it. The existing binding is preserved and no changes are made to the device.
- **Overwrite enabled:** The existing certificate binding is replaced with the newly enrolled certificate. The replacement is performed in-place — only the certificate reference on the virtual service is updated; no certificates are deleted from the device.

If the virtual service already has the **same** certificate bound (for example, during a renewal), the operation succeeds regardless of the Overwrite setting, and the binding is refreshed.

### Inventory

During an inventory job, the integration scans all virtual services on the device and returns the current binding information for each certificate in the Keyfactor certificate store. The `VirtualServiceBindings` entry parameter is populated with the comma-separated list of virtual services to which each certificate is currently bound, covering both SNI and non-SNI configurations. Certificates that exist in the Alteon certificate repository but are not bound to any virtual service are still included in the inventory with an empty `VirtualServiceBindings` value.

---

### License

[Apache](https://apache.org/licenses/LICENSE-2.0)