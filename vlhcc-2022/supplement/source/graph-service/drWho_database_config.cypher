MATCH (a:Character)
SET a.name = a.character
;
MATCH (a:Episode)
SET a.name = a.title
;
MATCH (a:Actor)
SET a.name = a.actor
;
MATCH (a:Planet)
SET a.name = a.planet
;
MATCH (a:Species)
SET a.name = a.species
;
MATCH (a:Thing)
SET a.name = a.thing
