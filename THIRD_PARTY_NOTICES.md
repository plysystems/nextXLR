# Third-Party Software Notices & Licenses

**NEXT : XLR** incorporates components, libraries, and tools from the following third-party open-source projects. We gratefully acknowledge the authors and contributors for their work.

---

## 1. Microsoft Windows App SDK & WinUI 3
- **Project**: Windows App SDK / WinUI
- **Website**: https://github.com/microsoft/WindowsAppSDK
- **License**: MIT License
- **Copyright**: Copyright (c) Microsoft Corporation. All rights reserved.

```text
The MIT License (MIT)

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 2. CommunityToolkit.Mvvm
- **Project**: Windows Community Toolkit (.NET Community Toolkit)
- **Website**: https://github.com/CommunityToolkit/dotnet
- **License**: MIT License
- **Copyright**: Copyright (c) .NET Foundation and Contributors. All rights reserved.

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 3. NAudio (NAudio.Core, NAudio.Wasapi)
- **Project**: NAudio Audio Library for .NET
- **Website**: https://github.com/naudio/NAudio
- **License**: MIT License
- **Copyright**: Copyright (c) Mark Heath.

```text
The MIT License (MIT)

Copyright (c) Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 4. Rust Engine & Daemon (`mixforge`)
The `mixforged` audio daemon utilizes the following open-source Rust crates:

### 4.1 cpal (Cross-Platform Audio Library)
- **Website**: https://github.com/RustAudio/cpal
- **License**: Apache License 2.0
- **Copyright**: Copyright (c) The CPAL Contributors

```text
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### 4.2 nusb (Cross-Platform USB Library)
- **Website**: https://github.com/kevinmehall/nusb
- **License**: MIT License / Apache License 2.0
- **Copyright**: Copyright (c) Katherine White and nusb contributors

### 4.3 rtrb (Real-Time Ring Buffer)
- **Website**: https://github.com/m-reiter/rtrb
- **License**: MIT License / Apache License 2.0
- **Copyright**: Copyright (c) Martin Robinson and rtrb contributors

### 4.4 tokio
- **Website**: https://github.com/tokio-rs/tokio
- **License**: MIT License
- **Copyright**: Copyright (c) Tokio Contributors

### 4.5 axum
- **Website**: https://github.com/tokio-rs/axum
- **License**: MIT License
- **Copyright**: Copyright (c) Tokio Contributors

### 4.6 serde & serde_json
- **Website**: https://github.com/serde-rs/serde
- **License**: MIT License / Apache License 2.0
- **Copyright**: Copyright (c) Erick Tryzelaar and David Tolnay

### 4.7 tracing & tracing-subscriber
- **Website**: https://github.com/tokio-rs/tracing
- **License**: MIT License
- **Copyright**: Copyright (c) Tokio Contributors

### 4.8 anyhow
- **Website**: https://github.com/dtolnay/anyhow
- **License**: MIT License / Apache License 2.0
- **Copyright**: Copyright (c) David Tolnay

### 4.9 futures-util
- **Website**: https://github.com/rust-lang/futures-rs
- **License**: MIT License / Apache License 2.0
- **Copyright**: Copyright (c) Rust Project and Tokio Contributors

---

## 5. Audio Plugin Specifications

### 5.1 CLAP (CLever Audio Plugin)
- **Website**: https://cleveraudio.org
- **License**: MIT License
- **Copyright**: Copyright (c) Alexandre Bique and Free-Audio contributors

```text
The MIT License (MIT)

Copyright (c) Alexandre Bique and Free-Audio contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 5.2 VST3 Protocol Compatibility
- VST is a trademark of Steinberg Media Technologies GmbH.
- NEXT : XLR interacts with user-installed VST3 plugins via standard host interfaces under open integration guidelines.

---

## 6. DSP & Machine Learning Algorithms
- **RNNoise**: Real-time recurrent neural network for noise suppression. Copyright (c) Jean-Marc Valin, Xiph.Org Foundation. Released under the BSD-3-Clause License.
- **DeepFilterNet**: Low-complexity speech enhancement framework. Released under the MIT / Apache 2.0 License.

---

## 7. Hardware Drivers
- TC-Helicon GoXLR Signed USB Audio Driver components are included solely to provide seamless hardware communication and device support on Windows operating systems for physical GoXLR devices. Copyright (c) MUSIC Tribe Global Services Ltd. / TC-Helicon. All trademarks and registered trademarks are the property of their respective owners.

