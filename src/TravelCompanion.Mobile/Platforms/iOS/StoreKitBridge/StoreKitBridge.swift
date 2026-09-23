import Foundation
import StoreKit

public typealias StoreCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> Void

private func emit(_ value: Any, context: UnsafeMutableRawPointer?, callback: StoreCallback?) {
    let data = (try? JSONSerialization.data(withJSONObject: value)) ?? Data("{}".utf8)
    let json = String(data: data, encoding: .utf8) ?? "{}"
    json.withCString { callback?(context, $0) }
}

private func errorMessage(_ error: Error) -> String {
    (error as NSError).localizedDescription
}

@_cdecl("yuku_store_query_product")
public func queryProduct(
    _ productId: UnsafePointer<CChar>,
    _ context: UnsafeMutableRawPointer?,
    _ callback: StoreCallback?
) {
    let identifier = String(cString: productId)
    Task {
        do {
            guard let product = try await Product.products(for: [identifier]).first else {
                emit(["available": false, "error": "product_unavailable"], context: context, callback: callback)
                return
            }
            emit([
                "available": true,
                "localizedPrice": product.displayPrice,
                "currencyCode": product.priceFormatStyle.currencyCode
            ], context: context, callback: callback)
        } catch {
            emit(["available": false, "error": errorMessage(error)], context: context, callback: callback)
        }
    }
}

@_cdecl("yuku_store_purchase")
public func purchase(
    _ productId: UnsafePointer<CChar>,
    _ accountToken: UnsafePointer<CChar>,
    _ context: UnsafeMutableRawPointer?,
    _ callback: StoreCallback?
) {
    let identifier = String(cString: productId)
    let token = UUID(uuidString: String(cString: accountToken))
    Task { @MainActor in
        do {
            guard let token else {
                emit(["state": "failed", "error": "invalid_app_account_token"], context: context, callback: callback)
                return
            }
            guard let product = try await Product.products(for: [identifier]).first else {
                emit(["state": "failed", "error": "product_unavailable"], context: context, callback: callback)
                return
            }
            switch try await product.purchase(options: [.appAccountToken(token)]) {
            case .success(let verification):
                switch verification {
                case .verified(let transaction):
                    emit(["state": "verifying", "evidence": verification.jwsRepresentation], context: context, callback: callback)
                case .unverified(_, let verificationError):
                    emit(["state": "failed", "error": errorMessage(verificationError)], context: context, callback: callback)
                }
            case .pending:
                emit(["state": "pending"], context: context, callback: callback)
            case .userCancelled:
                emit(["state": "cancelled"], context: context, callback: callback)
            @unknown default:
                emit(["state": "failed", "error": "unknown_storekit_result"], context: context, callback: callback)
            }
        } catch {
            emit(["state": "failed", "error": errorMessage(error)], context: context, callback: callback)
        }
    }
}

@_cdecl("yuku_store_restore")
public func restore(
    _ context: UnsafeMutableRawPointer?,
    _ callback: StoreCallback?
) {
    Task {
        do {
            try await AppStore.sync()
            var evidence: [String] = []
            for await result in Transaction.currentEntitlements {
                if case .verified(let transaction) = result {
                    evidence.append(result.jwsRepresentation)
                }
            }
            emit(["evidence": evidence], context: context, callback: callback)
        } catch {
            emit(["evidence": [], "error": errorMessage(error)], context: context, callback: callback)
        }
    }
}

@_cdecl("yuku_store_finish")
public func finishTransaction(
    _ transactionId: UnsafePointer<CChar>,
    _ context: UnsafeMutableRawPointer?,
    _ callback: StoreCallback?
) {
    let identifier = String(cString: transactionId)
    Task {
        for await result in Transaction.unfinished {
            if case .verified(let transaction) = result, String(transaction.id) == identifier {
                await transaction.finish()
                break
            }
        }
        emit(["finished": true], context: context, callback: callback)
    }
}
