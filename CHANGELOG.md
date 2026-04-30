1.3.0
  - Feature: support binding certificates to virtual service ID's
	- add entry parameter for virtual service id(s)
    - Now applying binding if virtual service ID is included in entry parameters
	- returning virtual service ID's as part of inventory
	
1.2.0
  - added apply and save actions after certificate operations
  
1.1.0
  - Add doctool and dual build for .net6/8

- 1.0.1
  - Switched order of cert/key submission
  - Added "renew=1" flag to add cert request if cert exists and overwrite==true 
  - additional logging

1.0.0
  - initial release