#include <iostream>
#include <fstream>
#include <string>
#include <regex>
#include <filesystem>

namespace fs = std::filesystem;

void ProcessFile(const fs::path& path, float multiplier = 5) {
    std::ifstream in(path);
    if (!in.is_open()) {
        std::cerr << "Failed to open file: " << path << "\n";
        return;
    }

    std::string text((std::istreambuf_iterator<char>(in)),
                     std::istreambuf_iterator<char>());
    in.close();

    // Regex: matches "Telecrystal: <integer>"
    std::regex priceRegex(R"(Telecrystal:\s*([0-9]+))");

    std::string output;
    output.reserve(text.size());

    std::sregex_iterator it(text.begin(), text.end(), priceRegex);
    std::sregex_iterator end;

    size_t lastPos = 0;
    bool updated = false;

    for (; it != end; ++it) {
        updated = true;
        const std::smatch& m = *it;
        size_t matchStart = m.position();
        size_t matchEnd = matchStart + m.length();

        // Copy text before match
        output.append(text.substr(lastPos, matchStart - lastPos));

        int price = std::stoi(m[1]);
        int newPrice = (float)price * multiplier;

        // Reconstruct line
        output.append("Telecrystal: " + std::to_string(newPrice));

        lastPos = matchEnd;
    }

    // Copy remaining text
    output.append(text.substr(lastPos));

    std::ofstream out(path, std::ios::trunc);
    if (!out.is_open()) {
        std::cerr << "Failed to write file: " << path << "\n";
        return;
    }

    out << output;
    out.close();

    if (updated)
        std::cout << "Updated: " << path << "\n";
}

void ProcessDirectory(const fs::path& root, float multiplier = 5) {
    for (auto& entry : fs::recursive_directory_iterator(root)) {
        if (!entry.is_regular_file())
            continue;

        auto ext = entry.path().extension().string();
        if (ext == ".yml" || ext == ".yaml")
            ProcessFile(entry.path(), multiplier);
    }
}

int main(int argc, char** argv) {
    if (argc < 3) {
        std::cout << "Arguments: <fp folder> <int multiplier>\n";
        return 1;
    }

    float multiplier = 5;
    try {
        multiplier = std::stof(argv[2]);
        std::cout << "Float = " << multiplier << "\n";
    }
    catch (const std::exception& e) {
        std::cerr << "Invalid multiplier.\n";
        return 1;
    }

    fs::path root = argv[1];
    if (!fs::exists(root)) {
        std::cerr << "Folder does not exist.\n";
        return 1;
    }

    ProcessDirectory(root, multiplier);
    return 0;
}
