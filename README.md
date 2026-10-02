idées :
on gère une ville (point de vu dieux) le but est d'empêcher les accidents en plaçant des éléments :
- stop
- feux
- passage piéton
- route barrée
- sens unique
- limitation de vitesse
- céder le passage
- limitation
- dos d'ane
- agent routier
- police

la boucle de jeux : pose des éléments > la scène se déroule > accident > recommence la scène au début
condition de victoire : toutes les voitures qui ont des quêtes remplissent leurs objectifs.
Dés qu'il y a un accident on recommence le niveau.
On a un nombre de ressource limité.

Il y a deux types de voitures : les voitures à quête (aller à une certaine destination) et les normales qui se déplacent plus aléatoirement pour faire de l'animation et créer des accidents avec des piétons ou d'autre voiture.
Mouvement des voitures normales:

Chaque décision est pseudo-aléatoire (pour avoir une répétition exactement pareil si on relance la sccène). Les décisions sont : la vitesse et le choix de la direction à chaque intersection.
La voiture détermine sa prochaine position par un raycast qui peut rencontrer soit une voiture devant, soit un piéton, soit une intersection soit un obstacle. Chacun de ces objets est correctement taggé.
La décision de faire un accident dépend des paramètres de vitesse, de la position des autres voitures, des panneau et aussi d'une variable pseudo-aléatoire.
Le comportement de la voiture est influencé par les panneaux qu'elle croise. Par exemple une limitation de vitesse ou un dos d'ane peux limiter la vitesse (sauf quelque excès pseudo-aléatoire), un policier peux arrêter une voiture en excès de vitesse, un stop ou un feu arrête la voiture un certain temps (exceptée des rares fautes qui peuvent être pénalisée par un élément policier), un céder le passage arrête la voiture si une autre viens de droite, les sens interdit empèche de prendre cette direction à une itersection...
Les panneaux sont des collider et dès que la voiture rentre dans un de ces collider elle récupère l'information du panneau et recalcule ses paramètres en fonction.
Le sintersections sont des collider avec l'information sur chacun d'eux si une route continue au nord, sud, est ou ouest.
