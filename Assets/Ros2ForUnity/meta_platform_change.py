import os

for root, dirs, files in os.walk(top="./"):
    for file in files:
        ext = os.path.splitext(file)[-1]
        filePath = os.path.join(root, file)
        if (ext == ".meta"):
            with open(filePath) as reader:
                content = reader.read()

            base = "settings: {}"
            out = "settings:\n        CPU: ARM64"
            content = content.replace(base, out, 1)

            with open(filePath, 'w') as writer:
                writer.write(content)

