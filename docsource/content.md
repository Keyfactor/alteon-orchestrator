## Overview

The Alteon Load Balancer integration allows you to manage certificates on a Radware Alteon Load Balancer appliance via its REST API. It supports inventory, enrollment (Add), and removal of certificates, and includes the ability to bind certificates to one or more virtual services as part of the enrollment workflow.

The integration handles both non-SNI (direct) and SNI certificate bindings automatically, detecting the appropriate path based on the current virtual service configuration on the device. SSL policies for non-SNI bindings are created and managed by the integration using a consistent naming convention. Apply and Save operations are performed automatically after each change to ensure configuration changes are activated and persisted on the device.