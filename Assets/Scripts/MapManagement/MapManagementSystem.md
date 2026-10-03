Cré La Map et son graphe associé étant donné un layout donné en ASCII (1 pour les routes 0 pour le reste)


################ SETUP ################
Placer RoadBuilder.cs sur un empty Road ------> cré la map
PathDebug ----> Visualise une trajectoire d'un sommet A vers B du graph dans scene


################ EXPOSED STUFF ################
RoadMapBuilder Instance contains the graph RoadMapBuilder.Graph (of Vector2Int nodes) 
RoadGraph -----> graph de la map (contient pleins de fonctions utiles)
RoadMapBuilder.WorldToGrid transform car position ⟼ graph node
graph.GetPath(A, B) to Get path from A to B   (or TryGetPath)
graph.RemoveEdges(edges) removes edges without changing visual layout