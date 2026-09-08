# Issue #34 dependency license inventory

Actual resolved Core graph, 2026-09-08. NuGet audit reported no known vulnerable packages. This is not a guarantee against future advisories.

New dependencies and upgraded Fonts use MIT or Apache-2.0 (CsvHelper selects Apache-2.0). NSax is absent. No commercial/online activation mechanism is introduced. Preserve copyright/license text; for Apache components retain applicable notices. The exact upstream texts accompany this inventory, including ImageSharp and .NET third-party notices. Include this directory with future distributions; no release artifact is produced or accepted by this change.

Package licenses were read from installed nuspec files. Source licenses use the package repository commit; MathNet uses upstream v5.0.0. Bundled NPOI/BouncyCastle/.NET notices come from installed packages. Existing ClosedXML graph is listed for traceability; its previous release licensing remains separately applicable.

| Package / resolved version | License expression | Copyright from nuspec | Source commit |
| --- | --- | --- | --- |
| BouncyCastle.Cryptography/2.6.2 | MIT | Copyright © Legion of the Bouncy Castle Inc. 2000-2025 | b4f2f6ad76bcd1f11f365ee50cc7447fbce79077 |
| ClosedXML/0.105.1 | MIT |  | b4ebe47bd3ecebf8480dea7422188d16b35b9d9a |
| ClosedXML.Parser/2.0.0 | MIT |  | 658973aeedc2fe289e0ccba2bb9959f67692ded5 |
| CsvHelper/33.1.0 | MS-PL OR Apache-2.0 | Copyright © 2009-2024 Josh Close | 5dad8b8b1d8b074f8353cfd482e939db788a8927 |
| DocumentFormat.OpenXml/3.1.1 | MIT | © Microsoft Corporation. All rights reserved. | f9e6ad7524e22b6b1f932deabc9055201b7870c0 |
| DocumentFormat.OpenXml.Framework/3.1.1 | MIT | © Microsoft Corporation. All rights reserved. | f9e6ad7524e22b6b1f932deabc9055201b7870c0 |
| Enums.NET/5.0.0 | MIT | Copyright © Tyler Brinkley 2016 | 1ce2a8836bc40c16fc99cb6ca4f4ceceba713724 |
| ExcelNumberFormat/1.1.0 | MIT |  |  |
| ExtendedNumerics.BigDecimal/2025.1001.2.129 | MIT | Adam White. MIT License. See License. | 8d3de28cf6ee59e9c6be864dacdba7d450c2a026 |
| MathNet.Numerics.Signed/5.0.0 | MIT | Copyright Math.NET Project |  |
| Microsoft.IO.RecyclableMemoryStream/3.0.1 | MIT | © Microsoft Corporation. All rights reserved. | e29a28387da9018fa9605a1dcb3f7a0435aa9974 |
| NPOI/2.7.4 | Apache-2.0 | Nissl LLC | 258ddcfce3bd6e843893bc71bd7b712587da299d |
| RBush.Signed/4.0.0 | MIT | Copyright © 2017-2024 Turning Code, LLC (and others) | b8690322abac35d835baa2fdca4a3918ed77a910 |
| SharpZipLib/1.4.2 | MIT | Copyright © 2000-2022 SharpZipLib Contributors | 33f64eb0f28cdd2b084cb822fcc224c7c5aba553 |
| SixLabors.Fonts/1.0.1 | Apache-2.0 | Copyright © Six Labors | be8883caf13f364292508274d68c39c2455ec3e2 |
| SixLabors.ImageSharp/2.1.11 | Apache-2.0 | Copyright © Six Labors | cb115c2293b63155ce29ee1e168024add20bcf28 |
| System.IO.Packaging/8.0.1 | MIT | © Microsoft Corporation. All rights reserved. | 81cabf2857a01351e5ab578947c7403a5b128ad1 |
| System.Security.Cryptography.Pkcs/10.0.11 | MIT | © Microsoft Corporation. All rights reserved. | e2f47b0110ed922f21a1522da67279133ce28f32 |
| System.Security.Cryptography.Xml/10.0.11 | MIT | © Microsoft Corporation. All rights reserved. | e2f47b0110ed922f21a1522da67279133ce28f32 |
| ZString/2.6.0 | MIT | © Cysharp, Inc. | 3156134075b89a162fe95350a2e92a4c85ccca59 |

## Snapshot integrity

SHA256 below refers to the UTF-8/LF repository text (license wording unchanged).

| File | SHA256 |
| --- | --- |
| BouncyCastle.Cryptography-2.6.2-LICENSE.md | EB52875C9B7DE8271DD835AF14FD598CDAA09E9E061F86E954CDA564EF6B06E6 |
| CsvHelper-33.1.0-LICENSE.txt | 9D4536859BFD0538AE5E846BCFBC030A77916DC2DF9A81219318F0B4813A6781 |
| dotnet-10.0.11-LICENSE.txt | CFC21F5E8BD655AE997EEC916138B707B1D290B83272C02A95C9F821B8C87310 |
| Enums.NET-5.0.0-LICENSE.txt | CA33AA7EBF14DC1A97B4D73070552C04981CD3E0F07F7F8FEBA4CAEF2B1DCD1B |
| ExtendedNumerics.BigDecimal-2025.1001.2.129-LICENSE.txt | 1E14F0923C9452B5494222B0ACC27818FA5CB9F00F2D28D91FC89B2E4886A15B |
| MathNet.Numerics.Signed-5.0.0-LICENSE.txt | 3F87FB0BEC6467D87760555E3CB246646A51912B0E97F432C159BA6BAF5738F2 |
| Microsoft.IO.RecyclableMemoryStream-3.0.1-LICENSE.txt | 7D6AA3550CF7AEB25EF542F616D8EAD585633E3B607C65E1DB4AA804E4971A91 |
| NPOI-2.7.4-LICENSE | 2792D42A8598E7569229BC04D95C108F910F11555A77CE5F144FD267A3C16E54 |
| SharpZipLib-1.4.2-LICENSE.txt | D7FBBC16F871BF24FA5624C7EAF945054B778985F32676DCACBA41FDDCA61133 |
| SixLabors.Fonts-1.0.1-LICENSE.txt | F38DC2765451FD2B19D7E0F47ED655F3A1CB889B28341EA6266EFD75D8425A6F |
| SixLabors.ImageSharp-2.1.11-LICENSE.txt | F38DC2765451FD2B19D7E0F47ED655F3A1CB889B28341EA6266EFD75D8425A6F |
| SixLabors.ImageSharp-2.1.11-NOTICES.txt | 063FA8B3C2087277807F6B9AA2156FCAEFF982265A8843E5F6D479D595B48B20 |
| System.Security.Cryptography.Pkcs-10.0.11-THIRD-PARTY-NOTICES.TXT | C6926930D0181FA515076603E1305461B1BCE5275A8B35337D75D2EEF3ACAD4A |
| System.Security.Cryptography.Xml-10.0.11-THIRD-PARTY-NOTICES.TXT | C6926930D0181FA515076603E1305461B1BCE5275A8B35337D75D2EEF3ACAD4A |
| ZString-2.6.0-LICENSE.txt | F3E6CFFB78D6D94FA37FD26ADA7294C3284B6B89940B5645F93ED24CDD5B36B0 |
