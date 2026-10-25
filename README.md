# TellMe

Réseau de messagerie décentralisé et asynchrone fondé sur un graphe acyclique dirigé causal (Merkle DAG) et un transport opportuniste pair-à-pair Bluetooth RFCOMM (Delay-Tolerant Network).

Projet d'ingénierie conçu et soutenu par **Fokam de loka wilfried** sous la direction de **Mr Dada Nelson** (Promotion 2026-2027, École Nationale Supérieure de Douala).

---

## 1. Vue d'Ensemble

TellMe est une infrastructure logicielle mobile permettant la communication textuelle sécurisée entre terminaux sans dépendance à Internet, aux réseaux cellulaires (3G/4G/5G), aux serveurs centraux ou aux services propriétaires Google Play. 

Conçu pour fonctionner dans des environnements contraints, partitionnés ou déconnectés (campus universitaires, zones blanches, contextes de crise civile ou rassemblements à forte densité), TellMe implémente le paradigme des **réseaux tolérants aux délais (DTN, Delay-Tolerant Networks)**. Les messages sont acheminés de proche en proche par le déplacement physique des utilisateurs, exploitant les opportunités de contact radio à courte portée.

L'intégrité globale et l'ordre causal des échanges sont régis par un **Merkle DAG (Directed Acyclic Graph)** local combiné à un protocole épidémique de synchronisation par tirage (*Merkle Pull Gossip*).

---

## 2. Principes Fondamentaux et Architecture

### 2.1 Causalité Logique vs Horloges Physiques
En environnement distribué déconnecté, les horloges matérielles des smartphones dérivent et ne peuvent être synchronisées via NTP. TellMe élimine tout recours aux horloges physiques pour l'ordonnancement. L'antériorité et la cohérence temporelle sont définies par des liens cryptographiques explicites vers un ou plusieurs messages parents :

$$\text{Hash} = \text{SHA256}(\text{Message} \parallel \text{From} \parallel \text{To} \parallel \text{Join}(",", \text{Sort}(\text{Parents})) \parallel \text{Look})$$

Le tri canonique préalable des identifiants parents garantit l'idempotence et l'invariance du calcul de hachage, indépendamment de l'ordre d'arrivée des paquets sur le réseau.

### 2.2 Gestion Tripartite de la Mémoire (`MessageStore`)
Le moteur causal local compartimente les données en trois structures :
* **MessageCollection :** Registre immuable des messages dont l'ensemble des ancêtres causaux est validé et présent localement.
* **CurrentsHeads :** Frontière active du graphe, contenant les sommets n'ayant aucun successeur direct connu.
* **Buffer (Sas d'attente des orphelins) :** Index inversé associant chaque identifiant manquant aux messages subordonnés en attente de résolution causale.

### 2.3 Modèle Cryptographique (Zéro Confiance)
* **Identité :** Paires de clés asymétriques générées localement selon la courbe elliptique NIST P-256 (ECDSA).
* **Authenticité et Intégrité :** Chaque message est signé cryptographiquement par son auteur. Tout message falsifié ou altéré en transit est rejeté dès l'entrée par clause de garde dans le moteur causal.
* **Confidentialité de Bout en Bout (E2EE) :** Chiffrement symétrique AES-256-CBC avec vecteur d'initialisation (IV) unique de 16 octets préfixé au texte chiffré.
* **Échange de Clés Déconnecté :** Établissement des canaux chiffrés par lecture optique bidirectionnelle de codes QR standardisés (PNG pur C# sans dépendance native).

### 2.4 Transport Physique 100% BLE GATT et Couplage Réseau
* **Canal Radio Déconnecté :** Bluetooth Low Energy (BLE GATT) en mode dual-role full-duplex sans appairage matériel ni saisie de code PIN.
  * *Pigeonnier (Serveur GATT) :* Service UUID `a6c8e3b0-28b9-4a4b-bb29-9e7b99c01123` avec caractéristique RX en Write (`...1124`) et TX en Notify (`...1125` avec CCCD).
  * *Pigeon Voyageur (Client GATT) :* Découverte de voisinage par `BleDiscoveryManager`, connexion directe en `BluetoothTransports.Le` et négociation dynamique du MTU à 512 octets.
* **Résilience de Flux :** Double moteur de découpage de trames combinant analyseur lexical `Utf8JsonReader` fondé sur `BytesConsumed` et préfixage binaire par longueur (4 octets Big-Endian).
* **Architecture UI Hybride :** Barre de navigation vectorielle Rive (`RiveBottomBar`) avec jeton séquentiel d'invalidation, combinée à une roue des perchoirs 100% native C# / XAML (`PerchWheelControl`) en coordonnées polaires pour une réactivité tactile instantanée.

---

## 3. Résultats Expérimentaux et Métriques Réelles

### 3.1 Banc d'Essai Matériel Réel
Les performances radio et la viabilité énergétique ont été validées sur un parc de smartphones physiques :
* **Infinix HOT 30 Play** (MediaTek Helio G88, Bluetooth 5.0, Android 13)
* **Xiaomi Redmi Note 9** (MediaTek Helio G85, Bluetooth 5.0, Android 11/12)
* **Xiaomi Redmi Note 14 5G** (Bluetooth 5.3, Android 14)

Sur 243 sessions physiques analysées :
* **Durée d'accroche radio :** 1.8 s à 2.7 s.
* **Échange effectif du Merkle DAG (Flash Sync) :** 0.69 s en moyenne sur Infinix, 0.75 s sur Redmi Note 9.
* **Portée radio utile :** 12 à 15 mètres en intérieur (traversée de cloisons, seuil RSSI à -88 dBm) et 25 à 30 mètres en champ libre.
* **Consommation énergétique :** 1.2% à 1.6% de batterie par heure en veille avec patrouille d'arrière-plan active (Foreground Service).

### 3.2 Simulations Virtuelles (Campus 700m x 700m)
* **Surface simulée :** 0.49 km² avec topologie piétonne réelle (amphis, salles de cours, restaurant universitaire, bibliothèque).
* **Population :** 250 étudiants dé-synchronisés (7 profils académiques, passants de couloirs servant de passerelles radio).
* **Résultats du scénario nominal (R = 60m, N = 250 nœuds) :** Taux de délivrance (PDR) de 81.25%, délai moyen de livraison de 17 minutes, 6.31 sauts moyens et 56.25% d'accusés de réception retournés.

---

## 4. Structure du Répertoire

```text
TellMe/
├── docs/
│   ├── MEMOIRE_TECHNIQUE_FOKAM_WILFRIED.docx   Mémoire universitaire complet au format Word
│   ├── MEMOIRE_TECHNIQUE.md                    Version Markdown du mémoire technique
│   ├── SIMULATION_RESULTS.md                   Données et matrices des 25 simulations
│   ├── SPEC.md                                 Spécifications formelles du protocole
│   ├── ROADMAP.md                              Feuille de route et jalons validés
│   └── DECISIONS.md                            Journal des décisions d'architecture (ADR)
├── TellMe.Core/                                Moteur agnostique multiplateforme (C# 12 / .NET 8)
│   ├── Engine/                                 Moteur causal (MessageStore, Façade TellMeEngine)
│   ├── Models/                                 Entités immuables (MeshMessage, Account, Contact)
│   ├── Network/                                Synchronisation (SyncEngine, ITransport, SyncPacket)
│   ├── Security/                               Cryptographie (ECDSA NIST P-256, AES-256-CBC)
│   └── Storage/                                Persistance SQLite (TellMeDatabase)
├── TellMe.App/                                 Application cliente .NET MAUI (Android)
│   ├── Controls/                               Composants et intégration vectorielle Rive
│   ├── Platforms/Android/                      Transport Bluetooth RFCOMM et handlers
│   ├── ViewModels/                             Présentation MVVM
│   └── Views/                                  Vues XAML (Accueil, Chat, Place Publique, Profil)
└── TellMe.Tests/                               Suite de 43 tests unitaires et d'intégration
```

---

## 5. Compilation et Exécution des Tests

### Prérequis
* Kit de développement logiciel .NET 8.0 SDK ou version ultérieure.
* Workload .NET MAUI (pour la cible Android de `TellMe.App`).
* Android SDK API 21 minimum (Android 5.0 Lollipop ou supérieur).

### Exécution de la Suite de Tests
La conformité algorithmique, cryptographique et réseau est validée par 43 tests automatisés (xUnit) :

```bash
dotnet test TellMe.Tests/TellMe.Tests.csproj --verbosity normal
```

Les cas de tests couvrent :
* L'invariance de hachage et l'idempotence du DAG causal.
* La résolution en cascade des messages orphelins dans le sas d'attente.
* La convergence épidémique multi-nœuds (simulation à 3 nœuds sans connectivité directe initiale).
* La résistance aux injections de paquets corrompus, non signés ou mal formés.
* Le découpage de flux continu et la fragmentation arbitraire au niveau MTU (990 octets).
* La persistance SQLite et la purge sélective des messages de transit (> 7 jours).

---

## 6. Documentation Complète

Pour une analyse exhaustive des fondements théoriques, des choix d'ingénierie, des post-mortems d'incidents techniques et des spécifications d'extension multimédia (PDF, images, chunking Merkle), consulter :

* [docs/MEMOIRE_TECHNIQUE_FOKAM_WILFRIED.docx](file:///c:/Users/UltraBook%203.1/Appmobile/TellMe/docs/MEMOIRE_TECHNIQUE_FOKAM_WILFRIED.docx) (Mémoire officiel au format Word)
* [docs/MEMOIRE_TECHNIQUE.md](file:///c:/Users/UltraBook%203.1/Appmobile/TellMe/docs/MEMOIRE_TECHNIQUE.md) (Version Markdown)
* [docs/SIMULATION_RESULTS.md](file:///c:/Users/UltraBook%203.1/Appmobile/TellMe/docs/SIMULATION_RESULTS.md) (Résultats détaillés des 25 simulations de campus)

---

## 7. Licence

Ce projet est distribué sous licence MIT. Consulter le fichier `LICENSE` pour les conditions applicables.
