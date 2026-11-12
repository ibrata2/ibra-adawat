import SwiftUI
import PhotosUI
import UniformTypeIdentifiers

/// A video picked from Photos, copied into the app's temporary folder.
struct PickedMovie: Transferable {
    let url: URL
    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(contentType: .movie) { SentTransferredFile($0.url) } importing: { received in
            let target = Outputs.inbox.appendingPathComponent(String(UUID().uuidString.prefix(6)) + "-" + received.file.lastPathComponent)
            try FileManager.default.copyItem(at: received.file, to: target)
            return PickedMovie(url: target)
        }
    }
}

struct HomeView: View {
    @EnvironmentObject private var store: Store
    @Environment(\.colorScheme) private var colorScheme
    @AppStorage("style") private var styleId = Style.glassRedWhite.rawValue
    @State private var photoItems: [PhotosPickerItem] = []
    @State private var showFiles = false
    @State private var showSettings = false
    @State private var loading = false
    @State private var alert: String?

    private var style: Style { Style(rawValue: styleId) ?? .glassRedWhite }
    private var section: ToolSection { Catalog.sections.first { $0.kind == store.section } ?? Catalog.sections[0] }

    var body: some View {
        NavigationStack {
            ZStack {
                style.background(dark: colorScheme == .dark).ignoresSafeArea()
                ScrollView {
                    VStack(spacing: 16) {
                        header
                        sectionPicker
                        filesCard
                        tools
                        if !store.jobs.isEmpty { jobs }
                    }
                    .padding(16)
                }
            }
            .toolbar(.hidden, for: .navigationBar)
        }
        .tint(style.accent)
        .preferredColorScheme(style.scheme)
        .sheet(isPresented: $showSettings) { SettingsView() }
        .fileImporter(isPresented: $showFiles, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
            if case let .success(urls) = result { store.receive(urls) }
        }
        .onChange(of: photoItems) { _, items in load(items) }
        .alert(alert ?? "", isPresented: Binding(get: { alert != nil }, set: { if !$0 { alert = nil } })) { Button("OK") { alert = nil } }
    }

    // MARK: Parts

    private var header: some View {
        HStack(spacing: 12) {
            Image("Logo").resizable().frame(width: 52, height: 52)
            VStack(alignment: .leading, spacing: 2) {
                Text("IBMC").font(.title2.bold())
                Text("Ibrahim Multimedia Converter").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button { showSettings = true } label: {
                Image(systemName: "gearshape.fill").font(.title3).padding(10).glass(14)
            }
        }
    }

    private var sectionPicker: some View {
        HStack(spacing: 8) {
            ForEach(Catalog.sections) { item in
                let selected = item.kind == store.section
                Button { withAnimation(.snappy) { store.section = item.kind } } label: {
                    VStack(spacing: 4) {
                        Image(systemName: item.symbol).font(.title3)
                        Text(item.title).font(.caption.bold()).lineLimit(1)
                    }
                    .frame(maxWidth: .infinity).padding(.vertical, 10)
                    .foregroundStyle(selected ? .white : .primary)
                    .background(selected ? AnyShapeStyle(style.accent) : AnyShapeStyle(.clear), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                }
                .buttonStyle(.plain)
            }
        }
        .padding(6).glass(20)
    }

    private var filesCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(store.inputs.isEmpty ? L.t("Choose files", "اختار الملفات") : L.t("\(store.inputs.count) files", "\(store.inputs.count) ملفات"))
                    .font(.headline)
                Spacer()
                if loading { ProgressView() }
                if !store.inputs.isEmpty {
                    Button(role: .destructive) { store.inputs.removeAll() } label: { Image(systemName: "xmark.circle.fill") }
                }
            }
            if !store.inputs.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(store.inputs, id: \.self) { url in
                            Label(url.lastPathComponent.split(separator: "-", maxSplits: 1).last.map(String.init) ?? url.lastPathComponent,
                                  systemImage: icon(url.kind))
                                .font(.caption).lineLimit(1).padding(.horizontal, 10).padding(.vertical, 6)
                                .background(.thinMaterial, in: Capsule())
                        }
                    }
                }
            }
            HStack(spacing: 10) {
                PhotosPicker(selection: $photoItems, matching: .any(of: [.images, .videos])) {
                    Label(L.t("Photos", "الصور"), systemImage: "photo.on.rectangle").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                Button { showFiles = true } label: {
                    Label(L.t("Files", "الملفات"), systemImage: "folder").frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
            }
        }
        .padding(14).glass()
    }

    private var tools: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 100), spacing: 10)], spacing: 10) {
            ForEach(section.tools) { tool in
                Button { use(tool) } label: { tile(tool) }.buttonStyle(.plain)
            }
        }
    }

    private func tile(_ tool: Tool) -> some View {
        VStack(spacing: 8) {
            if let badge = tool.badge {
                ZStack(alignment: .topLeading) {
                    RoundedRectangle(cornerRadius: 6).fill(.background.opacity(0.7)).frame(width: 40, height: 48)
                        .overlay(Image(systemName: section.symbol).foregroundStyle(style.accent).padding(.top, 14))
                    Text(badge).font(.system(size: 10, weight: .heavy)).foregroundStyle(.white).padding(.horizontal, 4).padding(.vertical, 2)
                        .background(style.accent, in: RoundedRectangle(cornerRadius: 4)).offset(x: -6, y: 2)
                }
                .environment(\.layoutDirection, .leftToRight)
            } else {
                Image(systemName: tool.symbol).font(.system(size: 28)).foregroundStyle(style.accent).frame(height: 48)
            }
            Text(tool.title).font(.caption.weight(.semibold)).multilineTextAlignment(.center).lineLimit(2).minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, minHeight: 104).padding(8).glass(16)
    }

    private var jobs: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(L.t("Tasks", "المهام")).font(.headline)
                Spacer()
                Button(L.t("Clear", "مسح")) { store.jobs.removeAll { $0.state != .running } }.font(.caption)
            }
            ForEach(store.jobs) { job in JobRow(job: job) }
        }
        .padding(14).glass()
    }

    // MARK: Actions

    private func use(_ tool: Tool) {
        let files = store.inputs.filter(tool.fits)
        guard !files.isEmpty else {
            alert = L.t("Add files for this tool first, with Photos or Files.", "ضيف ملفات لهاي الأداة أول شي، من الصور أو الملفات.")
            return
        }
        guard files.count >= tool.minFiles else {
            alert = L.t("This tool needs at least \(tool.minFiles) files.", "هاي الأداة تحتاج \(tool.minFiles) ملفات على الأقل.")
            return
        }
        store.run(tool, on: files)
    }

    private func load(_ items: [PhotosPickerItem]) {
        guard !items.isEmpty else { return }
        loading = true
        Task {
            var urls: [URL] = []
            for item in items {
                if item.supportedContentTypes.contains(where: { $0.conforms(to: .movie) }), let movie = try? await item.loadTransferable(type: PickedMovie.self) {
                    urls.append(movie.url)
                } else if let data = try? await item.loadTransferable(type: Data.self) {
                    let ext = item.supportedContentTypes.first?.preferredFilenameExtension ?? "jpg"
                    let url = Outputs.inbox.appendingPathComponent(String(UUID().uuidString.prefix(6)) + "-photo.\(ext)")
                    if (try? data.write(to: url)) != nil { urls.append(url) }
                }
            }
            store.add(urls)
            photoItems = []
            loading = false
        }
    }

    private func icon(_ kind: Kind) -> String {
        switch kind {
        case .video: return "film"
        case .audio: return "music.note"
        case .image: return "photo"
        case .pdf: return "doc.richtext"
        case .other: return "doc"
        }
    }
}

struct JobRow: View {
    @ObservedObject var job: Job
    @State private var saved = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: job.state == .done ? "checkmark.circle.fill" : job.state == .failed ? "exclamationmark.triangle.fill" : "hourglass")
                    .foregroundStyle(job.state == .done ? .green : job.state == .failed ? .red : .secondary)
                Text(job.title).font(.subheadline.bold())
                Spacer()
                Text(job.files.count == 1 ? (job.files[0].lastPathComponent.split(separator: "-", maxSplits: 1).last.map(String.init) ?? "") : L.t("\(job.files.count) files", "\(job.files.count) ملفات"))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            if job.state == .running { ProgressView(value: job.progress) }
            if !job.message.isEmpty { Text(job.message).font(.caption).foregroundStyle(job.state == .failed ? .red : .secondary) }
            if !job.outputs.isEmpty {
                HStack {
                    ShareLink(items: job.outputs) { Label(L.t("Share / Save", "مشاركة / حفظ"), systemImage: "square.and.arrow.up") }
                        .buttonStyle(.bordered)
                    if job.outputs.contains(where: { $0.kind == .image || $0.kind == .video }) {
                        Button {
                            Task { saved = await Store.saveToPhotos(job.outputs) }
                        } label: { Label(saved ? L.t("Saved", "انحفظت") : L.t("To Photos", "إلى الصور"), systemImage: saved ? "checkmark" : "photo.badge.plus") }
                        .buttonStyle(.bordered)
                    }
                }
                .font(.caption)
            }
        }
        .padding(10)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}
