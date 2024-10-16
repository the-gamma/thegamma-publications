# Graph-service

This service has for objective to allow The Gamma to handle Neo4j graph databases. This service with both the REST and the PIVOT type provider. 

## Setting up
To run it localy: 

- You need to download the neo4j environement https://neo4j.com/ and to run the drWho database available here https://neo4j.com/developer/example-data/. 

- You will also need to run the gallery-csv-service: https://github.com/the-gamma/gallery-csv-service .

- Then you will need to set up the drWho database so it can work with the service. To do that, you will need to allow multiple query in the setting of neo4j and then run the script drWho_database_config.cypher .

- You will also need to download all the libraries to run the service, to do that just use `npm install` 

## Run it

Then to run it you can just type `node app.js`. Then if you want to visualise it, you can use the sample-web editor https://github.com/the-gamma/thegamma-sample-web. 


## Things to come and more information

Shortly, we will try to run it on a server so it would be easier to use it. Also, shortly, we will try to add new databases to this service as the panama-papers database. You can also find more information on this service in the report which is in the publication folder of this repository. Also here is the link of the slides of the presentation of this service https://prezi.com/p/uahizyttz32s/transparent-data-visualizationof-graph-databases-with-the-gamma/ 

