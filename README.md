# Orchestrator Extension for Alteon Load Balancer

The Alteon Load Balancer integration allows you to manage certificates within the Alteon Load Balancer device.

#### Integration status: Prototype - Demonstration quality. Not for use in customer environments.

## About the Keyfactor Universal Orchestrator Capability

This repository contains a Universal Orchestrator Capability which is a plugin to the Keyfactor Universal Orchestrator. Within the Keyfactor Platform, Orchestrators are used to manage “certificate stores” &mdash; collections of certificates and roots of trust that are found within and used by various applications.

The Universal Orchestrator is part of the Keyfactor software distribution and is available via the Keyfactor customer portal. For general instructions on installing Capabilities, see the “Keyfactor Command Orchestrator Installation and Configuration Guide” section of the Keyfactor documentation. For configuration details of this specific Capability, see below in this readme.

The Universal Orchestrator is the successor to the Windows Orchestrator. This Capability plugin only works with the Universal Orchestrator and does not work with the Windows Orchestrator.

---



## Platform Specific Notes

The Keyfactor Universal Orchestrator may be installed on either Windows or Linux based platforms. The certificate operations supported by a capability may vary based what platform the capability is installed on. The table below indicates what capabilities are supported based on which platform the encompassing Universal Orchestrator is running.
| Operation | Win | Linux |
|-----|-----|------|
|Supports Management Add|&check; |&check; |
|Supports Management Remove|&check; |&check; |
|Supports Create Store|  |  |
|Supports Discovery|  |  |
|Supports Renrollment|  |  |
|Supports Inventory|&check; |&check; |



---

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

### Install the Extension on the Orchestrator

_The process for installing an extension for the universal orchestrator differs from the process of installing an extension for the Windows orchestrator.  Follow the below steps to register the integration with your instance of the universal orchestrator._

1) Stop the Universal Orchestrator service.

     1) Note: In Windows, this service is called "Keyfactor Orchestrator Service (Default)"

1) Create a folder in the "extensions" folder of the Universal Orchestrator installation folder named "AlteonLB"

     1) example: `C:\Program Files\Keyfactor\Keyfactor Orchestrator\\_AlteonLB_

1) Copy the build output (if you compiled from source) or the contents of the zip file (if you downloaded the pre-compiled binaries) into this folder.

1) Start the Universal Orchestrator Service

### Create the certificate store

Now add the certificate store that corresponds to an instance of the Alteon Load Balancer.

The steps to do this are:

1) Navigate to "Locations > Certificate Stores"

1) Click "ADD"

     ![Approve Cert Store](/Images/add-cert-store-button.png)

1) Enter the values corresponding to the Azure Keyvault instance.

- **Category**: Alteon Load Balancer
- **Container**: _optional logical container in keyfactor for the certificates from this store_
- **Client Machine**: The Alteon Load Balancer Server and port

  - Note: The server credentials will only have to be entered once, even if adding multiple certificate stores.  
    - Set the credentials to those of the account with sufficient permissions to manage certs in the Alteon Load Balancer.

- **Store Path**: This value isn't used for this integration (other than to uniquely identify the cert store in certificate searches).  

---

### License

[Apache](https://apache.org/licenses/LICENSE-2.0)
